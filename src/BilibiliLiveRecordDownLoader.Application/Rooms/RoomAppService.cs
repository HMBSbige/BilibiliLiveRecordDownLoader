namespace BilibiliLiveRecordDownLoader.Application.Rooms;

internal sealed class RoomAppService(BilibiliApiClient apiClient, IAppConfigManager configManager, RoomSessionFactory sessionFactory) : IRoomAppService
{
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

	private void Touch()
	{
		Interlocked.Increment(ref _version);
	}
}
