using System.Runtime.CompilerServices;

namespace BilibiliLiveRecordDownLoader.Application.Rooms;

internal sealed class RoomAppService(BilibiliApiClient apiClient, IAppConfigManager configManager, RoomSessionFactory sessionFactory) : IRoomAppService
{
	private const int RoomInfoBatchSize = 10;

	private readonly Lock _lock = new();
	private readonly List<RoomSession> _sessions = [];

	// 已经移除、还在释放的房间，弹幕写入者可能还要写一会儿；退出时也要等它们，最后一段的后处理才会在退出前登记
	private readonly List<Removal> _closing = [];
	private long _version;

	public long Version => Interlocked.Read(ref _version);

	// 删除后还在收尾的房间也算，退出前要确认
	public bool IsRecording
	{
		get
		{
			lock (_lock)
			{
				return _closing.Count > 0 || _sessions.Exists(static session => session.IsRecording);
			}
		}
	}

	public IReadOnlyList<RoomSnapshot> GetRooms()
	{
		lock (_lock)
		{
			return [.. _sessions.Select(static session => session.GetSnapshot())];
		}
	}

	public async Task StartAsync(CancellationToken cancellationToken = default)
	{
		AppConfig config = await configManager.GetAsync(cancellationToken);

		lock (_lock)
		{
			// 不持配置锁也能枚举：房间列表是只读的，修改时整个换掉
			foreach (RoomConfig room in config.Rooms.Where(room => Find(room.RoomId) is null))
			{
				RoomSession session = sessionFactory.Create(room, Touch);
				_sessions.Add(session);
				session.Start();
			}
		}

		Touch();
	}

	public async Task StopAsync()
	{
		RoomSession[] sessions;
		Task[] closing;

		lock (_lock)
		{
			sessions = [.. _sessions];
			closing = [.. _closing.Select(static removal => removal.Closed.Task)];
			_sessions.Clear();
		}

		Touch();
		await Task.WhenAll(sessions.Select(static session => session.DisposeAsync().AsTask()).Concat(closing));
	}

	public async Task<RoomSnapshot> AddAsync(RoomSettingsDto settings, CancellationToken cancellationToken = default)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(settings.RoomId);

		// 用户可能输入短号，以接口返回的真实房间号为准
		LiveRoomInfo info;

		try
		{
			info = await RequestTimeout.ReportAsync(apiClient.GetRoomInfoAsync(settings.RoomId, cancellationToken));
		}
		catch (Exception exception) when (exception is BilibiliApiException { Code: BilibiliApiException.RoomNotFoundCode } || BilibiliErrors.IsBlocked(exception))
		{
			throw new InvalidOperationException(BilibiliErrors.Describe(exception, settings.RoomId), exception);
		}

		RoomConfig room = (settings with { RoomId = info.RoomId }).ToConfig();
		RoomSession session;

		lock (_lock)
		{
			if (Find(room.RoomId) is not null)
			{
				throw new InvalidOperationException(AppLanguage.Current.RoomAlreadyExists.ToString(room.RoomId));
			}

			session = sessionFactory.Create(room, Touch);
			_sessions.Add(session);
		}

		session.Apply(info);
		session.Start();
		Touch();

		await UpdateConfiguredRoomsAsync(rooms => rooms.Append(room));

		return session.GetSnapshot();
	}

	public async Task<IReadOnlyList<RoomAddResult>> AddRangeAsync(IReadOnlyList<RoomSettingsDto> settings, CancellationToken cancellationToken = default)
	{
		long[] pending;

		lock (_lock)
		{
			pending = [.. settings.Select(static item => item.RoomId).Where(roomId => roomId > 0 && FindByAnyId(roomId) is null).Distinct()];
		}

		Dictionary<long, string> errors = [];
		Dictionary<long, LiveRoomInfo> resolved = await ResolveAsync(pending, errors, cancellationToken);

		RoomAddResult[] results = new RoomAddResult[settings.Count];
		List<(RoomSession Session, RoomConfig Room, LiveRoomInfo Info)> created = [];

		lock (_lock)
		{
			for (int index = 0; index < settings.Count; ++index)
			{
				results[index] = AddResolved(settings[index], resolved, errors, created);
			}
		}

		foreach ((RoomSession session, _, LiveRoomInfo info) in created)
		{
			session.Apply(info);
			session.Start();
		}

		Touch();

		if (created.Count > 0)
		{
			await UpdateConfiguredRoomsAsync(configured => configured.Concat(created.Select(static added => added.Room)));
		}

		return results;
	}

	public async IAsyncEnumerable<FollowedRoomDto> GetFollowedRoomsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		await using IAsyncEnumerator<FollowedLiveRoom> rooms = apiClient.GetFollowedRoomsAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);

		while (await MoveNextFollowedAsync(rooms))
		{
			FollowedLiveRoom room = rooms.Current;
			bool isAdded;

			lock (_lock)
			{
				isAdded = FindByAnyId(room.RoomId) is not null;
			}

			yield return new FollowedRoomDto(room.RoomId, room.UserName, room.Title, room.LiveStatus, isAdded);
		}
	}

	private static async ValueTask<bool> MoveNextFollowedAsync(IAsyncEnumerator<FollowedLiveRoom> rooms)
	{
		try
		{
			return await RequestTimeout.ReportAsync(rooms.MoveNextAsync());
		}
		catch (BilibiliApiException exception) when (exception.Code is BilibiliApiException.NotLoggedInCode)
		{
			throw new LoginRequiredException(exception);
		}
		catch (Exception exception) when (BilibiliErrors.IsBlocked(exception))
		{
			throw new InvalidOperationException(AppLanguage.Current.RequestRateLimited, exception);
		}
	}

	public async Task UpdateAsync(RoomSettingsDto settings, CancellationToken cancellationToken = default)
	{
		RoomConfig room = settings.ToConfig();
		RoomSession session;

		lock (_lock)
		{
			session = Find(room.RoomId) ?? throw new InvalidOperationException(AppLanguage.Current.InvalidRoomId);
		}

		session.UpdateSettings(room);

		await UpdateConfiguredRoomsAsync(rooms => rooms.Select(existing => existing.RoomId == room.RoomId ? room : existing));
	}

	public async Task MoveAsync(IReadOnlyCollection<long> roomIds, long? before, CancellationToken cancellationToken = default)
	{
		HashSet<long> moving = [.. roomIds];

		lock (_lock)
		{
			if (!RoomOrder.MoveBefore(_sessions, static session => session.RoomId, moving, before))
			{
				return;
			}
		}

		Touch();

		await UpdateConfiguredRoomsAsync(rooms =>
		{
			List<RoomConfig> moved = [.. rooms];
			RoomOrder.MoveBefore(moved, static room => room.RoomId, moving, before);
			return moved;
		});
	}

	public async Task RemoveAsync(IReadOnlyCollection<long> roomIds, CancellationToken cancellationToken = default)
	{
		Removal removal;

		lock (_lock)
		{
			removal = new Removal([.. _sessions.Where(session => roomIds.Contains(session.RoomId))]);
			_sessions.RemoveAll(session => roomIds.Contains(session.RoomId));
			_closing.Add(removal);
		}

		Touch();

		try
		{
			await UpdateConfiguredRoomsAsync(rooms => rooms.Where(room => !roomIds.Contains(room.RoomId)));
		}
		finally
		{
			try
			{
				await Task.WhenAll(removal.Sessions.Select(static session => session.DisposeAsync().AsTask()));
			}
			finally
			{
				lock (_lock)
				{
					_closing.Remove(removal);
				}

				removal.Closed.SetResult();
			}
		}
	}

	// 和添加房间一样分批查询：刷新全部房间时逐个并发请求更容易被风控拦截，拦截之后录制也拿不到候选流
	public async Task RefreshAsync(IReadOnlyCollection<long> roomIds, CancellationToken cancellationToken = default)
	{
		(RoomSession Session, long DanmuVersion)[] targets;

		lock (_lock)
		{
			targets = [.. _sessions.Where(session => roomIds.Contains(session.RoomId)).Select(static session => (session, session.DanmuVersion))];
		}

		Dictionary<long, string> errors = [];
		Dictionary<long, LiveRoomInfo> infos = await ResolveAsync([.. targets.Select(static target => target.Session.RoomId)], errors, cancellationToken);

		foreach ((RoomSession session, long danmuVersion) in targets)
		{
			if (infos.GetValueOrDefault(session.RoomId) is { } info)
			{
				session.Apply(info, danmuVersion);
			}
		}

		if (errors.Count > 0)
		{
			throw new InvalidOperationException(string.Join(Environment.NewLine, errors.Values.Distinct(StringComparer.Ordinal)));
		}
	}

	public async Task<string> GetDirectoryAsync(long roomId, CancellationToken cancellationToken = default)
	{
		AppConfig config = await configManager.GetAsync(cancellationToken);
		return RecordingFileLayout.GetRoomDirectory(config.RecordDirectory, roomId);
	}

	internal RoomSession? FindSession(long roomId)
	{
		lock (_lock)
		{
			return Find(roomId);
		}
	}

	internal bool IsRecordingDanmakuFile(string path)
	{
		lock (_lock)
		{
			return _sessions.Concat(_closing.SelectMany(static removal => removal.Sessions)).Any(session => session.Danmaku.IsWriting(path));
		}
	}

	private RoomSession? Find(long roomId)
	{
		return _sessions.Find(session => session.RoomId == roomId);
	}

	// 关注列表等来源给出的房间号可能是短号
	private RoomSession? FindByAnyId(long roomId)
	{
		return roomId > 0 ? _sessions.Find(session => session.RoomId == roomId || session.ShortId == roomId) : null;
	}

	// 在锁内调用；新会话先加进列表，由调用方到锁外应用房间信息并启动
	private RoomAddResult AddResolved(RoomSettingsDto settings, Dictionary<long, LiveRoomInfo> resolved, Dictionary<long, string> errors, List<(RoomSession Session, RoomConfig Room, LiveRoomInfo Info)> created)
	{
		long requested = settings.RoomId;

		if (requested <= 0)
		{
			return new RoomAddResult.Failed(requested, AppLanguage.Current.InvalidRoomId);
		}

		if (!resolved.TryGetValue(requested, out LiveRoomInfo? info))
		{
			return FindByAnyId(requested) is not null ? new RoomAddResult.Skipped(requested) : new RoomAddResult.Failed(requested, errors.GetValueOrDefault(requested, AppLanguage.Current.InvalidRoomId));
		}

		// 同一批里的短号和长号可能指向同一个房间
		if (Find(info.RoomId) is not null)
		{
			return new RoomAddResult.Skipped(requested);
		}

		RoomConfig room = (settings with { RoomId = info.RoomId }).ToConfig();
		RoomSession session = sessionFactory.Create(room, Touch);
		_sessions.Add(session);
		created.Add((session, room, info));
		return new RoomAddResult.Added(requested);
	}

	private async Task<Dictionary<long, LiveRoomInfo>> ResolveAsync(IReadOnlyList<long> roomIds, Dictionary<long, string> errors, CancellationToken cancellationToken)
	{
		Dictionary<long, LiveRoomInfo> resolved = [];
		List<long> missing = [];

		try
		{
			foreach (long[] chunk in roomIds.Chunk(RoomInfoBatchSize))
			{
				IReadOnlyList<LiveRoomInfo> infos;

				try
				{
					infos = await apiClient.GetRoomInfosAsync(chunk, cancellationToken);
				}
				catch (BilibiliApiException exception) when (!BilibiliErrors.IsBlocked(exception))
				{
					missing.AddRange(chunk);
					continue;
				}

				foreach (long roomId in chunk)
				{
					if (infos.FirstOrDefault(candidate => candidate.RoomId == roomId || candidate.ShortId == roomId) is { } info)
					{
						resolved[roomId] = info;
					}
					else
					{
						missing.Add(roomId);
					}
				}
			}

			foreach (long roomId in missing)
			{
				try
				{
					resolved[roomId] = await apiClient.GetRoomInfoAsync(roomId, cancellationToken);
				}
				catch (BilibiliApiException exception) when (!BilibiliErrors.IsBlocked(exception))
				{
					errors[roomId] = BilibiliErrors.Describe(exception, roomId);
				}
			}
		}
		// 网络错误或被风控拦截时后面的请求也会失败，继续请求只会更容易被拦截
		catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
		{
			string error = BilibiliErrors.Describe(exception);

			foreach (long roomId in roomIds.Where(roomId => !resolved.ContainsKey(roomId)))
			{
				errors.TryAdd(roomId, error);
			}
		}

		return resolved;
	}

	// 会话已经改好了，配置不能因为调用方取消而跟它对不上
	private ValueTask UpdateConfiguredRoomsAsync(Func<IEnumerable<RoomConfig>, IEnumerable<RoomConfig>> change)
	{
		return configManager.UpdateAsync(config => config.Rooms = [.. change(config.Rooms)], CancellationToken.None);
	}

	private void Touch()
	{
		Interlocked.Increment(ref _version);
	}

	// 释放失败由 RemoveAsync 报告，Closed 只表示收尾结束
	private sealed class Removal(RoomSession[] sessions)
	{
		public RoomSession[] Sessions { get; } = sessions;

		public TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
	}
}
