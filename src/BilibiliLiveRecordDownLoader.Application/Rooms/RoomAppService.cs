using System.Runtime.CompilerServices;

namespace BilibiliLiveRecordDownLoader.Application.Rooms;

internal sealed class RoomAppService(BilibiliApiClient apiClient, IAppConfigManager configManager, RoomSessionFactory sessionFactory) : IRoomAppService
{
	private const int RoomInfoBatchSize = 10;

	private readonly Lock _lock = new();
	private readonly List<RoomSession> _sessions = [];
	private long _version;

	public long Version => Interlocked.Read(ref _version);

	public bool IsRecording
	{
		get
		{
			lock (_lock)
			{
				return _sessions.Any(static session => session.IsRecording);
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

		lock (_lock)
		{
			sessions = [.. _sessions];
			_sessions.Clear();
		}

		Touch();
		await Task.WhenAll(sessions.Select(static session => session.DisposeAsync().AsTask()));
	}

	public async Task<RoomSnapshot> AddAsync(RoomSettingsDto settings, CancellationToken cancellationToken = default)
	{
		if (settings.RoomId <= 0)
		{
			throw new ArgumentException(AppLanguage.Current.InvalidRoomId);
		}

		// 用户可能输入短号，以接口返回的真实房间号为准
		LiveRoomInfo info = await apiClient.GetRoomInfoAsync(settings.RoomId, cancellationToken);
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

		await configManager.UpdateAsync(config => config.Rooms.Add(room), CancellationToken.None);

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
		List<(int Index, RoomSession Session, LiveRoomInfo Info)> created = [];
		List<RoomConfig> rooms = [];

		lock (_lock)
		{
			for (int index = 0; index < settings.Count; ++index)
			{
				RoomSettingsDto item = settings[index];
				long requested = item.RoomId;

				if (requested <= 0)
				{
					results[index] = new RoomAddResult.Failed(requested, AppLanguage.Current.InvalidRoomId);
				}
				else if (resolved.TryGetValue(requested, out LiveRoomInfo? info))
				{
					// 同一批里的短号和长号可能指向同一个房间
					if (Find(info.RoomId) is { } existing)
					{
						results[index] = new RoomAddResult.Skipped(requested, existing.RoomId);
						continue;
					}

					RoomConfig room = (item with { RoomId = info.RoomId }).ToConfig();
					RoomSession session = sessionFactory.Create(room, Touch);
					_sessions.Add(session);
					rooms.Add(room);
					created.Add((index, session, info));
				}
				else if (FindByAnyId(requested) is { } existing)
				{
					results[index] = new RoomAddResult.Skipped(requested, existing.RoomId);
				}
				else
				{
					results[index] = new RoomAddResult.Failed(requested, errors.GetValueOrDefault(requested, AppLanguage.Current.InvalidRoomId));
				}
			}
		}

		foreach ((int index, RoomSession session, LiveRoomInfo info) in created)
		{
			session.Apply(info);
			session.Start();
			results[index] = new RoomAddResult.Added(settings[index].RoomId, session.GetSnapshot());
		}

		Touch();

		if (rooms.Count > 0)
		{
			await configManager.UpdateAsync(config => config.Rooms.AddRange(rooms), CancellationToken.None);
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

	// 迭代器的 yield 不能放在带 catch 的 try 里，所以单独转换翻页时的异常
	private static async ValueTask<bool> MoveNextFollowedAsync(IAsyncEnumerator<FollowedLiveRoom> rooms)
	{
		try
		{
			return await rooms.MoveNextAsync();
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

		await configManager.UpdateAsync
		(
			config =>
			{
				int index = config.Rooms.FindIndex(existing => existing.RoomId == room.RoomId);

				if (index >= 0)
				{
					config.Rooms[index] = room;
				}
			},
			CancellationToken.None
		);

		Touch();
	}

	public async Task RemoveAsync(IReadOnlyCollection<long> roomIds, CancellationToken cancellationToken = default)
	{
		RoomSession[] removed;

		lock (_lock)
		{
			removed = [.. _sessions.Where(session => roomIds.Contains(session.RoomId))];
			_sessions.RemoveAll(session => roomIds.Contains(session.RoomId));
		}

		Touch();

		try
		{
			await configManager.UpdateAsync(config => config.Rooms.RemoveAll(room => roomIds.Contains(room.RoomId)), CancellationToken.None);
		}
		finally
		{
			await Task.WhenAll(removed.Select(static session => session.DisposeAsync().AsTask()));
		}
	}

	public async Task RefreshAsync(IReadOnlyCollection<long> roomIds, CancellationToken cancellationToken = default)
	{
		RoomSession[] targets;

		lock (_lock)
		{
			targets = [.. _sessions.Where(session => roomIds.Contains(session.RoomId))];
		}

		await Task.WhenAll(targets.Select(session => session.RefreshAsync(cancellationToken)));
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

	// 一律以接口返回的真实房间号添加。批量查询的结果里缺少的房间，或接口拒绝了批量查询时，再逐个查询
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
					if (infos.FirstOrDefault(info => info.RoomId == roomId || info.ShortId == roomId) is { } info)
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
					errors[roomId] = exception.Message;
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

	private void Touch()
	{
		Interlocked.Increment(ref _version);
	}
}
