namespace BilibiliLiveRecordDownLoader.Application.Rooms;

internal sealed partial class RoomSession(RoomConfig settings, RoomSessionServices services, Action onChanged) : IRecordingObserver, IAsyncDisposable
{
	// 段关闭之后等它的弹幕文件写完；写入者卡住时不让后处理一直等下去
	internal static readonly TimeSpan DanmakuCloseTimeout = TimeSpan.FromMinutes(2);

	private readonly Lock _lock = new();
	private readonly CancellationTokenSource _lifetime = new();
	private readonly ILogger _logger = services.Logger.WithRoomScope(settings.RoomId);
	private RoomConfig _settings = settings;
	private long _shortId;
	// 房间信息还没拿到时为 0
	private long _userId;
	private string? _userName;
	private string? _title;
	private string _area = string.Empty;
	private string _parentArea = string.Empty;
	private string _liveId = string.Empty;
	private DateTimeOffset? _liveStartTime;
	private bool _isTextOnlyActive;
	private LiveStatus _liveStatus = LiveStatus.Unknown;
	private RecordStatus _recordStatus;
	private double _bytesPerSecond;
	private string? _recordingPath;
	private SplitControl? _split;
	private ActiveRecordingDto? _activeRecording;
	private string? _lastError;
	private CancellationTokenSource? _monitorCancellation;
	private Task _monitorTask = Task.CompletedTask;
	private CancellationTokenSource? _recordingCancellation;
	private Task _recordingTask = Task.CompletedTask;
	private bool _isRecordingLoopActive;
	private bool _disposed;

	public long RoomId { get; } = settings.RoomId;

	internal RoomDanmaku Danmaku { get; } = new(settings.RoomId, services.TimeProvider, services.Logger.WithRoomScope(settings.RoomId), onChanged);

	public long ShortId
	{
		get
		{
			lock (_lock)
			{
				return _shortId;
			}
		}
	}

	public bool IsRecording
	{
		get
		{
			lock (_lock)
			{
				return _recordStatus is not RecordStatus.Idle;
			}
		}
	}

	private RoomConfig Settings
	{
		get
		{
			lock (_lock)
			{
				return _settings;
			}
		}
	}

	private string? Title
	{
		get
		{
			lock (_lock)
			{
				return _title;
			}
		}
	}

	private string StreamerName
	{
		get
		{
			lock (_lock)
			{
				return string.IsNullOrEmpty(_userName) ? RoomId.ToString(CultureInfo.InvariantCulture) : _userName;
			}
		}
	}

	private Uri LiveRoomUri => new(AppConsts.LiveRoomUrlPrefix + RoomId.ToString(CultureInfo.InvariantCulture));

	public void Start()
	{
		lock (_lock)
		{
			RestartMonitors();
		}
	}

	public RoomSnapshot GetSnapshot()
	{
		lock (_lock)
		{
			return new RoomSnapshot(_settings.ToDto(), _shortId, _userName, _title, _liveStatus, _recordStatus, _bytesPerSecond, _activeRecording, _lastError) { Danmaku = Danmaku.Snapshot };
		}
	}

	public void UpdateSettings(RoomConfig settings)
	{
		lock (_lock)
		{
			RoomConfig previous = _settings;
			_settings = settings;

			if (previous.StatusCheckInterval != settings.StatusCheckInterval || previous.DanmuReconnectDelay != settings.DanmuReconnectDelay)
			{
				RestartMonitors();
			}

			if (!settings.IsRecordingEnabled)
			{
				_recordingCancellation?.Cancel();
			}

			if (previous.IsDanmakuEnabled != settings.IsDanmakuEnabled || previous.DanmakuOffset != settings.DanmakuOffset)
			{
				Danmaku.OnSettingsChanged();
			}
		}

		UpdateDanmaku();
		onChanged();
		TryStartRecording();
	}

	public async Task RefreshAsync(CancellationToken cancellationToken = default)
	{
		Apply(await services.ApiClient.GetRoomInfoAsync(RoomId, cancellationToken));
	}

	public void Apply(LiveRoomInfo info)
	{
		lock (_lock)
		{
			_shortId = info.ShortId;
			_userId = info.UserId;
			_userName = info.UserName;
			_area = info.AreaName;
			_parentArea = info.ParentAreaName;
			_liveId = info.LiveId;
			_liveStartTime = info.LiveStartTime;
		}

		SetTitle(info.Title);
		SetLiveStatus(info.LiveStatus);
		onChanged();
	}

	public async ValueTask DisposeAsync()
	{
		Task monitor;
		Task recording;

		lock (_lock)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			monitor = _monitorTask;
			recording = _recordingTask;
		}

		await _lifetime.CancelAsync();
		await Task.WhenAll(monitor, recording);

		// 录制循环已经结束，写入者把排队的内容写完再关闭文件
		UpdateDanmaku();
		await Danmaku.DisposeAsync();

		lock (_lock)
		{
			_monitorCancellation?.Dispose();
			_monitorCancellation = null;
		}

		_lifetime.Dispose();
	}

	void IRecordingObserver.OnRecordingStarted(string path, RecordingStream stream, IRecordingTimeline timeline)
	{
		lock (_lock)
		{
			_recordingPath = path;
			_activeRecording = new ActiveRecordingDto(stream.Format, stream.Codec, stream.Quality, stream.Restriction, services.TimeProvider.GetUtcNow());
			_recordStatus = RecordStatus.Recording;
			Danmaku.OnVideoStarted(timeline, path, CreateDanmakuRun());
		}

		onChanged();
	}

	// 路径已经被录制循环取走时，这一段已经交给了后处理
	void IRecordingObserver.OnSegmentCompleted(string path)
	{
		lock (_lock)
		{
			if (_recordingPath != path)
			{
				return;
			}

			_recordingPath = null;
		}

		_ = PostProcessAsync(path, Danmaku.CompleteSegment(path));
	}

	// 分段只换文件，录制时长接着这次连接算
	void IRecordingObserver.OnSegmentStarted(string path)
	{
		lock (_lock)
		{
			_recordingPath = path;
			Danmaku.OnSegmentStarted(path);
		}
	}

	void IRecordingObserver.OnSpeedChanged(double bytesPerSecond)
	{
		lock (_lock)
		{
			_bytesPerSecond = bytesPerSecond;
		}

		onChanged();
	}

	private void RestartMonitors()
	{
		if (_disposed)
		{
			return;
		}

		_monitorCancellation?.Cancel();
		_monitorCancellation?.Dispose();
		_monitorCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);

		CancellationToken cancellationToken = _monitorCancellation.Token;
		_monitorTask = Task.WhenAll(_monitorTask, RunDanmuLoopAsync(cancellationToken), RunStatusPollingAsync(_settings.StatusCheckInterval, cancellationToken));
	}

	private async Task RunDanmuLoopAsync(CancellationToken cancellationToken)
	{
		await Task.Yield();
		using IDisposable? scope = _logger.BeginRoomScope(RoomId);
		ReconnectBackoff backoff = new(Settings.DanmuReconnectDelay, services.TimeProvider, Random.Shared);

		while (!cancellationToken.IsCancellationRequested)
		{
			TimeSpan delay;
			backoff.OnConnecting();
			Danmaku.OnConnecting();

			try
			{
				await foreach (DanmuMessage message in services.ReceiveDanmu(RoomId, Danmaku.Options, cancellationToken))
				{
					// 认证成功后马上就有这两条，算作收到消息会让认证后立刻断开的连接一直不退避
					if (message is not (DanmuMessage.Connected or DanmuMessage.LoginNotice))
					{
						backoff.OnReceived();
					}

					Danmaku.OnReceived(message);
					Handle(message);
				}

				// 取消时枚举正常结束，不算断线
				if (cancellationToken.IsCancellationRequested)
				{
					return;
				}

				delay = backoff.NextDelay();
				LogDanmuDisconnected(_logger, delay);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception exception)
			{
				delay = backoff.NextDelay();
				LogDanmuFailed(_logger, exception, delay);
			}

			// 取消时不算断开：重启监控时新的连接可能已经在建立
			Danmaku.OnDisconnected();

			try
			{
				await Task.Delay(delay, services.TimeProvider, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				return;
			}
		}
	}

	private async Task RunStatusPollingAsync(TimeSpan interval, CancellationToken cancellationToken)
	{
		await Task.Yield();
		using IDisposable? scope = _logger.BeginRoomScope(RoomId);
		using PeriodicTimer timer = new(interval, services.TimeProvider);

		try
		{
			do
			{
				await TryRefreshAsync(cancellationToken);
			}
			while (await timer.WaitForNextTickAsync(cancellationToken));
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// 房间停止监控
		}
	}

	internal void Handle(DanmuMessage message)
	{
		(LiveStatus? status, string? title) = message switch
		{
			DanmuMessage.LiveStarted => (LiveStatus.Live, null),
			DanmuMessage.LiveStopped { IsRound: true } => (LiveStatus.Round, null),
			DanmuMessage.LiveStopped => (LiveStatus.Idle, null),
			DanmuMessage.RoomChanged changed => ((LiveStatus?)null, SetArea(changed)),
			DanmuMessage.Connected or DanmuMessage.LoginNotice or DanmuMessage.Comment or DanmuMessage.SuperChat or DanmuMessage.SuperChatDeleted or DanmuMessage.Gift or DanmuMessage.GuardBought or DanmuMessage.Raw => (null, null)
		};

		if (title is not null)
		{
			SetTitle(title);
		}

		if (status is { } value)
		{
			SetLiveStatus(value);
		}
	}

	private string SetArea(DanmuMessage.RoomChanged changed)
	{
		lock (_lock)
		{
			_area = changed.AreaName ?? _area;
			_parentArea = changed.ParentAreaName ?? _parentArea;
		}

		return changed.Title;
	}

	private void SetTitle(string title)
	{
		bool notify;
		SplitControl? split;

		lock (_lock)
		{
			if (_title == title)
			{
				return;
			}

			// 启动或添加房间时第一次拿到的标题不算更改
			notify = _title is not null && _settings.IsNotificationEnabled;
			split = _title is not null && _recordStatus is RecordStatus.Recording ? _split : null;

			// 和分段、开始仅弹幕的工作在同一把锁里排队，标题记录不会排到它们前面
			if (_title is not null)
			{
				Danmaku.OnTitleChanged(title);
			}

			_title = title;
		}

		LogTitleChanged(_logger, title);
		onChanged();

		// 是否按标题分段由切分时的设置决定
		split?.RequestTitleSplit();

		if (notify)
		{
			_ = NotifyTitleChangedAsync(title);
		}
	}

	private async Task NotifyTitleChangedAsync(string title)
	{
		if ((await services.ConfigManager.GetAsync()).NotifyOnTitleChange)
		{
			services.Notifications.Show(AppLanguage.Current.TitleChanged.ToString(StreamerName), title, link: LiveRoomUri);
		}
	}

	private void SetLiveStatus(LiveStatus status)
	{
		string? title;
		bool notify;

		lock (_lock)
		{
			if (_liveStatus == status)
			{
				return;
			}

			_liveStatus = status;
			title = _title;
			notify = _settings.IsNotificationEnabled;
		}

		LogLiveStatusChanged(_logger, status);
		UpdateDanmaku();
		onChanged();

		if (status is not LiveStatus.Live)
		{
			return;
		}

		if (notify)
		{
			services.Notifications.Show(AppLanguage.Current.LiveStarted.ToString(StreamerName), title ?? string.Empty);
		}

		TryStartRecording();
	}

	private void TryStartRecording()
	{
		lock (_lock)
		{
			if (_disposed || _isRecordingLoopActive || !_settings.IsRecordingEnabled || _liveStatus is not LiveStatus.Live)
			{
				return;
			}

			_isRecordingLoopActive = true;
			_recordingCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
			_recordingTask = RunRecordingLoopAsync(_recordingCancellation.Token);
		}

		UpdateDanmaku();
	}

	// 开了录弹幕、没开录制时，开播期间仅录弹幕；录制循环还在收尾时等它结束，免得两边同时写
	private void UpdateDanmaku()
	{
		lock (_lock)
		{
			bool isTextOnly = !_disposed && _settings is { IsDanmakuEnabled: true, IsRecordingEnabled: false } && !_isRecordingLoopActive && _liveStatus is LiveStatus.Live;

			if (isTextOnly != _isTextOnlyActive)
			{
				_isTextOnlyActive = isTextOnly;

				if (isTextOnly)
				{
					Danmaku.StartTextOnly(CreateDanmakuRun());
				}
				else
				{
					Danmaku.StopTextOnly();
					Danmaku.EndSession();
				}
			}

			Danmaku.SetRecording(!_disposed && _settings.IsDanmakuEnabled && (_isRecordingLoopActive || isTextOnly));
		}
	}

	private DanmakuRun CreateDanmakuRun()
	{
		return new DanmakuRun(CreateDanmakuHeader, GetDanmakuSettingsAsync);
	}

	private DanmakuFileHeader CreateDanmakuHeader()
	{
		lock (_lock)
		{
			return new DanmakuFileHeader
			{
				RoomId = RoomId,
				ShortId = _shortId,
				Streamer = _userName ?? string.Empty,
				Title = _title ?? string.Empty,
				Area = _area,
				ParentArea = _parentArea,
				LiveId = _liveId,
				LiveStartTime = _liveStartTime
			};
		}
	}

	private async ValueTask<DanmakuRunSettings> GetDanmakuSettingsAsync(CancellationToken cancellationToken)
	{
		AppConfig config = await services.ConfigManager.GetAsync(cancellationToken);
		RoomConfig room = Settings;
		return new DanmakuRunSettings(DanmakuProfile.Create(config, room), config.RecordDirectory, config.IncludeTitleInFileName, RecordingSplitPolicy.Create(config, room).OnTitleChange);
	}

	private bool ShouldKeepRecording(CancellationToken cancellationToken)
	{
		lock (_lock)
		{
			return !cancellationToken.IsCancellationRequested && _settings.IsRecordingEnabled && _liveStatus is LiveStatus.Live;
		}
	}

	private async Task RunRecordingLoopAsync(CancellationToken cancellationToken)
	{
		await Task.Yield();
		using IDisposable? scope = _logger.BeginRoomScope(RoomId);
		using IDisposable diskSpaceWatch = services.DiskSpaceMonitor.Watch();
		LiveRecordingSession liveSession = new();
		RecordingFaultTracker faults = new(services.TimeProvider);
		SetLastError(null);

		try
		{
			while (ShouldKeepRecording(cancellationToken))
			{
				SetConnecting();
				AppConfig config = await services.ConfigManager.GetAsync(cancellationToken);
				RecordingProfile profile = RecordingProfile.Create(config, Settings);

				// 配置管理器一直返回同一个实例，设置页的修改直接改在它上面
				SplitControl split = SetSplitControl(new SplitControl(() => RecordingSplitPolicy.Create(config, Settings), () => config.RemoveFillerData));

				try
				{
					// 每建一个文件按当时的设置和标题命名
					RecordingAttempt attempt = await services.Recorder.RecordAsync(RoomId, Volatile.Read(ref _userId), profile, liveSession, this, split, () => config.IncludeTitleInFileName ? Title : null, cancellationToken);

					// 流结束时尽快确认是否已下播，下播造成的断流不算线路故障
					await TryRefreshAsync(cancellationToken);

					if (!attempt.IsQuickFailure)
					{
						faults.OnRecorded();
					}

					if (ShouldKeepRecording(cancellationToken) && attempt.End is RecordingEnd.WriteFailed { Exception: var writeFailure })
					{
						// 不标记线路失败也不换线；正常录制过一段时间后和断流一样回到首选流
						if (!attempt.IsQuickFailure)
						{
							liveSession.Reset();
						}

						ReportWriteFailure(faults, config, writeFailure);
						await WaitAfterFailureAsync(writeFailure, profile.StreamReconnectDelay, cancellationToken);
					}
					else if (ShouldKeepRecording(cancellationToken) && attempt.IsQuickFailure)
					{
						// 立即换下一条线路，全部试过之后才会等待重连间隔
						liveSession.MarkFailed(attempt.Candidate, attempt.Endpoint);
						LogAttemptFailed(attempt);
						SetLastError($"{attempt.Candidate.Name} {attempt.Endpoint.Host}: {attempt.End.Description}");

						if (!liveSession.HasUntriedEndpoint)
						{
							// 所有线路都试过一遍，等待之后重新请求候选流从头再试
							liveSession.Reset();
							LogAllEndpointsFailed(_logger, profile.StreamReconnectDelay);
							ReportStall(faults, config);
							await Task.Delay(profile.StreamReconnectDelay, services.TimeProvider, cancellationToken);
						}
					}
					else
					{
						// 正常录制过一段时间，之前失败的线路可能已经恢复，从首选流重新请求签名地址
						liveSession.Reset();
						LogRecordingEnded(_logger, attempt.End.Description);
					}
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception exception)
				{
					// 拉不到流也可能是已经下播，确认之后不再当作失败重试；停止录制时不必再查
					if (!cancellationToken.IsCancellationRequested)
					{
						await TryRefreshAsync(cancellationToken);

						if (!ShouldKeepRecording(cancellationToken))
						{
							LogRecordingEnded(_logger, exception.Message);
							continue;
						}

						// 这里的失败都发生在开始录制之前：建不了目录或文件是写盘失败，其余都是拿不到能用的线路
						if (exception is RecordingWriteException writeFailure)
						{
							ReportWriteFailure(faults, config, writeFailure);
						}
						else
						{
							ReportStall(faults, config);
						}
					}

					await WaitAfterFailureAsync(exception, profile.StreamReconnectDelay, cancellationToken);
				}
				finally
				{
					if (TakeRecordingPath() is { } path)
					{
						_ = PostProcessAsync(path, Danmaku.CompleteSegment(path));
					}

					Danmaku.OnVideoEnded();
				}
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			LogRecordingStopped(_logger);
		}
		catch (Exception exception)
		{
			LogRecordingFailed(_logger, exception, TimeSpan.Zero);
		}
		finally
		{
			lock (_lock)
			{
				_isRecordingLoopActive = false;
				_recordStatus = RecordStatus.Idle;
				_activeRecording = null;
				_bytesPerSecond = 0;
				_recordingCancellation?.Dispose();
				_recordingCancellation = null;
				Danmaku.EndSession();
			}

			UpdateDanmaku();
			onChanged();

			// 循环退出的同时可能又收到了开播消息
			TryStartRecording();
		}
	}

	private void ReportStall(RecordingFaultTracker faults, AppConfig config)
	{
		if (faults.TryReportStall() && config.NotifyOnRecordingError)
		{
			services.Notifications.Show(AppLanguage.Current.RecordingStalled.ToString(StreamerName), AppLanguage.Current.RecordingStalledMessage, AppNotificationType.Error);
		}
	}

	private void ReportWriteFailure(RecordingFaultTracker faults, AppConfig config, RecordingWriteException exception)
	{
		if (faults.TryReportWriteFailure() && config.NotifyOnRecordingError)
		{
			services.Notifications.Show(AppLanguage.Current.RecordingWriteFailed.ToString(StreamerName), exception.Message, AppNotificationType.Error);
		}
	}

	private async Task WaitAfterFailureAsync(Exception exception, TimeSpan delay, CancellationToken cancellationToken)
	{
		LogRecordingFailed(_logger, exception, delay);
		SetLastError(exception.Message);
		await Task.Delay(delay, services.TimeProvider, cancellationToken);
	}

	private async Task TryRefreshAsync(CancellationToken cancellationToken)
	{
		try
		{
			await RefreshAsync(cancellationToken);
		}
		catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
		{
			// HttpClient 超时也是 OperationCanceledException，只有自身被取消时才向上传递
			LogRefreshFailed(_logger, exception);
		}
	}

	// 每次重连都会先回到连接中，这时上一路流已经不再录制
	private void SetConnecting()
	{
		lock (_lock)
		{
			_recordStatus = RecordStatus.Connecting;
			_activeRecording = null;
		}

		onChanged();
	}

	private void SetLastError(string? error)
	{
		lock (_lock)
		{
			_lastError = error;
		}

		onChanged();
	}

	private SplitControl SetSplitControl(SplitControl split)
	{
		lock (_lock)
		{
			_split = split;
		}

		return split;
	}

	// 是否转封装按写完这一段时的设置；弹幕文件写完关闭之后才能生成字幕和章节
	private async Task PostProcessAsync(string path, Task<DanmakuSegment?> danmaku)
	{
		AppConfig config = await services.ConfigManager.GetAsync();
		RecordingProfile profile = RecordingProfile.Create(config, Settings);
		DanmakuProfile danmakuProfile = DanmakuProfile.Create(config, Settings);
		DanmakuSegment? segment;

		try
		{
			segment = await danmaku.WaitAsync(DanmakuCloseTimeout, services.TimeProvider);
		}
		catch (TimeoutException)
		{
			LogDanmakuCloseTimedOut(_logger, path);
			segment = null;
		}

		await services.PostProcessor.ProcessAsync(path, profile, StreamerName, segment, danmakuProfile);
	}

	private string? TakeRecordingPath()
	{
		lock (_lock)
		{
			string? path = _recordingPath;
			_recordingPath = null;
			return path;
		}
	}

	[LoggerMessage(Level = LogLevel.Warning, Message = "The danmaku file of {Path} is still being written, post-processing it without danmaku")]
	private static partial void LogDanmakuCloseTimedOut(ILogger logger, string path);

	[LoggerMessage(Level = LogLevel.Information, Message = "Danmu connection closed, reconnecting in {Delay}")]
	private static partial void LogDanmuDisconnected(ILogger logger, TimeSpan delay);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Danmu connection failed, reconnecting in {Delay}")]
	private static partial void LogDanmuFailed(ILogger logger, Exception exception, TimeSpan delay);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to refresh room status")]
	private static partial void LogRefreshFailed(ILogger logger, Exception exception);

	[LoggerMessage(Level = LogLevel.Information, Message = "Title: {Title}")]
	private static partial void LogTitleChanged(ILogger logger, string title);

	[LoggerMessage(Level = LogLevel.Information, Message = "Live status: {Status}")]
	private static partial void LogLiveStatusChanged(ILogger logger, LiveStatus status);

	private void LogAttemptFailed(RecordingAttempt attempt)
	{
		(string stream, string host) = (attempt.Candidate.Name, attempt.Endpoint.Host);

		if (attempt.End is RecordingEnd.Faulted { Exception: var exception })
		{
			LogAttemptFaulted(_logger, exception, stream, host, attempt.ActiveDuration);
		}
		else
		{
			LogAttemptEnded(_logger, stream, host, attempt.ActiveDuration, attempt.End.Description);
		}
	}

	[LoggerMessage(Level = LogLevel.Warning, Message = "Every stream failed, retrying in {Delay}")]
	private static partial void LogAllEndpointsFailed(ILogger logger, TimeSpan delay);

	[LoggerMessage(Level = LogLevel.Information, Message = "Recording ended: {Reason}")]
	private static partial void LogRecordingEnded(ILogger logger, string reason);

	[LoggerMessage(Level = LogLevel.Warning, Message = "{Stream} stream from {Host} failed after {Duration} of data, switching to the next stream")]
	private static partial void LogAttemptFaulted(ILogger logger, Exception exception, string stream, string host, TimeSpan duration);

	[LoggerMessage(Level = LogLevel.Warning, Message = "{Stream} stream from {Host} ended after {Duration} of data ({Reason}), switching to the next stream")]
	private static partial void LogAttemptEnded(ILogger logger, string stream, string host, TimeSpan duration, string reason);

	[LoggerMessage(Level = LogLevel.Information, Message = "Recording stopped")]
	private static partial void LogRecordingStopped(ILogger logger);

	[LoggerMessage(Level = LogLevel.Error, Message = "Recording failed, retrying in {Delay}")]
	private static partial void LogRecordingFailed(ILogger logger, Exception exception, TimeSpan delay);
}
