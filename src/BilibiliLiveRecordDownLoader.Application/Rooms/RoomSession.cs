using System.Diagnostics.CodeAnalysis;

namespace BilibiliLiveRecordDownLoader.Application.Rooms;

internal sealed partial class RoomSession(RoomConfig settings, RoomSessionServices services, Action onChanged) : IRecordingObserver, IAsyncDisposable
{
	private static readonly TimeSpan DanmakuCloseTimeout = TimeSpan.FromMinutes(2);

	// 网络盘断开时卡住的写入常常等到会话超时才以 IO 错误结束；退出时也在等后处理，不能一直等下去
	private static readonly TimeSpan RecordingCloseTimeout = TimeSpan.FromMinutes(2);

	private readonly Lock _lock = new();
	private readonly CancellationTokenSource _lifetime = new();
	private readonly ILogger _logger = services.Logger.WithRoomScope(settings.RoomId);
	private RoomConfig _settings = settings;
	private long _shortId;
	private string? _userName;
	private string? _title;
	private string _area = string.Empty;
	private string _parentArea = string.Empty;
	private string _liveId = string.Empty;
	private DateTimeOffset? _liveStartTime;
	private bool _isTextOnlyActive;
	private LiveStatus _liveStatus = LiveStatus.Unknown;
	private long _danmuVersion;
	// 弹幕最后一次推送直播状态、标题时的版本：比查询前取得的版本新时，应答里的对应部分已经过时
	private long _liveStatusVersion;
	private long _titleVersion;
	private RecordStatus _recordStatus;
	private double _bytesPerSecond;
	private string? _recordingPath;
	private Task _recordingClosed = Task.CompletedTask;
	private SplitControl? _split;
	private ActiveRecordingDto? _activeRecording;
	private string? _lastError;
	private Task _danmuTask = Task.CompletedTask;
	private CancellationTokenSource? _statusCancellation;
	private Task _statusTask = Task.CompletedTask;
	[SuppressMessage("Usage", "CA2213", Justification = "录制循环退出时释放，DisposeAsync 会等循环结束")]
	private CancellationTokenSource? _recordingCancellation;
	private Task _recordingTask = Task.CompletedTask;
	private bool _isRecordingLoopActive;
	private bool _isStarted;
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
				return _recordStatus is not RecordStatus.Idle || _isTextOnlyActive;
			}
		}
	}

	// 查询房间信息之前取得，和应答一起交给 Apply
	public long DanmuVersion
	{
		get
		{
			lock (_lock)
			{
				return _danmuVersion;
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

	private Uri LiveRoomUri { get; } = new(AppConsts.LiveRoomUrlPrefix + settings.RoomId.ToString(CultureInfo.InvariantCulture));

	public void Start()
	{
		lock (_lock)
		{
			if (_disposed || _isStarted)
			{
				return;
			}

			_isStarted = true;
			_danmuTask = RunDanmuLoopAsync(_lifetime.Token);
			_ = RestartStatusPolling();
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
		CancellationTokenSource? statusCancellation = null;

		lock (_lock)
		{
			RoomConfig previous = _settings;
			_settings = settings;

			// 弹幕重连间隔在下次重连时读取，不必为它断开弹幕连接
			if (_isStarted && previous.StatusCheckInterval != settings.StatusCheckInterval)
			{
				statusCancellation = RestartStatusPolling();
			}

			if (!settings.IsRecordingEnabled)
			{
				// 循环结束时在这把锁里释放它，所以在锁内取消；CancelAsync 不会在持锁的线程上内联执行收尾
				_ = _recordingCancellation?.CancelAsync();
			}

			if (previous.IsDanmakuEnabled != settings.IsDanmakuEnabled || previous.DanmakuOffset != settings.DanmakuOffset)
			{
				Danmaku.OnSettingsChanged();
			}
		}

		if (statusCancellation is not null)
		{
			statusCancellation.Cancel();
			statusCancellation.Dispose();
		}

		UpdateDanmaku();
		onChanged();
		TryStartRecording();
	}

	public async Task RefreshAsync(CancellationToken cancellationToken = default)
	{
		long danmuVersion = DanmuVersion;
		Apply(await services.ApiClient.GetRoomInfoAsync(RoomId, cancellationToken), danmuVersion);
	}

	// 查询之后弹幕推送过的部分不用应答覆盖：直播状态连同本场信息，标题连同分区
	public void Apply(LiveRoomInfo info, long? danmuVersion = null)
	{
		lock (_lock)
		{
			_shortId = info.ShortId;
			_userName = info.UserName;

			if (IsCurrent(_titleVersion, danmuVersion))
			{
				_area = info.AreaName;
				_parentArea = info.ParentAreaName;
			}

			if (IsCurrent(_liveStatusVersion, danmuVersion))
			{
				_liveId = info.LiveId;
				_liveStartTime = info.LiveStartTime;
			}
		}

		SetTitle(info.Title, danmuVersion);
		SetLiveStatus(info.LiveStatus, danmuVersion);
		onChanged();
	}

	public async ValueTask DisposeAsync()
	{
		Task danmu;
		Task status;
		Task recording;

		lock (_lock)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			danmu = _danmuTask;
			status = _statusTask;
			recording = _recordingTask;
		}

		await _lifetime.CancelAsync();

		try
		{
			await Task.WhenAll(danmu, status, recording);
		}
		finally
		{
			UpdateDanmaku();
			await Danmaku.DisposeAsync();

			lock (_lock)
			{
				_statusCancellation?.Dispose();
				_statusCancellation = null;
			}

			_lifetime.Dispose();
		}
	}

	void IRecordingObserver.OnRecordingStarted(string path, RecordingStream stream, IRecordingTimeline timeline, Task closed)
	{
		lock (_lock)
		{
			_recordingPath = path;
			_recordingClosed = closed;
			_activeRecording = new ActiveRecordingDto(stream.Format, stream.Codec, stream.Quality, stream.Restriction, stream.QualityNames, services.TimeProvider.GetUtcNow());
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

		// 在录制任务里同步登记，会话释放时等录制任务结束就不会漏掉这一段的后处理
		services.PostProcessor.Track(PostProcessAsync(path, Task.CompletedTask, Danmaku.CompleteSegment(path)));
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

	// 在锁内调用；返回被替换的取消源，由调用方到锁外取消，免得循环在持锁的线程上收尾
	private CancellationTokenSource? RestartStatusPolling()
	{
		if (_disposed)
		{
			return null;
		}

		CancellationTokenSource? previous = _statusCancellation;
		_statusCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
		_statusTask = Task.WhenAll(_statusTask, RunStatusPollingAsync(_settings.StatusCheckInterval, _statusCancellation.Token));
		return previous;
	}

	private async Task RunDanmuLoopAsync(CancellationToken cancellationToken)
	{
		// 调用方可能在 UI 线程上，Task.Yield 会把循环投回它的同步上下文
		await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
		using IDisposable? scope = _logger.BeginRoomScope(RoomId);
		ReconnectBackoff backoff = new(() => Settings.DanmuReconnectDelay, services.TimeProvider, Random.Shared);

		while (!cancellationToken.IsCancellationRequested)
		{
			TimeSpan delay;
			backoff.OnConnecting();
			Danmaku.OnConnecting();

			try
			{
				await foreach (DanmuMessage message in services.ReceiveDanmu(RoomId, Danmaku.Options, cancellationToken))
				{
					// 认证后马上就有连接成功和未登录通知，算作收到会让认证后立刻断开的连接一直不退避
					// 原始消息只是存档副本，存不存它都不应影响退避
					if (message is not (DanmuMessage.Connected or DanmuMessage.LoginNotice or DanmuMessage.Raw))
					{
						backoff.OnReceived();
					}

					Danmaku.OnReceived(message);
					Handle(message);
				}

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

			Danmaku.OnDisconnected();

			await Task.Delay(delay, services.TimeProvider, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
		}
	}

	private async Task RunStatusPollingAsync(TimeSpan interval, CancellationToken cancellationToken)
	{
		await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
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
		}
	}

	internal void Handle(DanmuMessage message)
	{
		LiveStatus? status = message switch
		{
			DanmuMessage.LiveStarted => LiveStatus.Live,
			DanmuMessage.LiveStopped { IsRound: true } => LiveStatus.Round,
			DanmuMessage.LiveStopped => LiveStatus.Idle,
			DanmuMessage.RoomChanged or DanmuMessage.Connected or DanmuMessage.LoginNotice or DanmuMessage.Comment or DanmuMessage.SuperChat or DanmuMessage.SuperChatDeleted or DanmuMessage.Gift or DanmuMessage.GuardBought or DanmuMessage.Raw => null
		};

		// 标题分段会立刻用分区写新文件的文件头，先更新分区
		if (message is DanmuMessage.RoomChanged changed)
		{
			UpdateAreaFromDanmu(changed);
			SetTitle(changed.Title);
		}

		if (status is { } value)
		{
			SetLiveStatusFromDanmu(value);
		}
	}

	private void UpdateAreaFromDanmu(DanmuMessage.RoomChanged changed)
	{
		lock (_lock)
		{
			_titleVersion = ++_danmuVersion;
			_area = changed.AreaName ?? _area;
			_parentArea = changed.ParentAreaName ?? _parentArea;
		}
	}

	private void SetLiveStatusFromDanmu(LiveStatus status)
	{
		lock (_lock)
		{
			_liveStatusVersion = ++_danmuVersion;

			// 开播消息不带本场的 live_id 和开播时间，留着的是上一场的值
			if (status is LiveStatus.Live && _liveStatus is not LiveStatus.Live)
			{
				_liveId = string.Empty;
				_liveStartTime = null;
			}
		}

		SetLiveStatus(status);
	}

	// 调用方在写入的同一把锁里比较，Apply 更新房间信息之后才到的弹幕消息也能让旧应答作废
	private static bool IsCurrent(long pushedVersion, long? danmuVersion)
	{
		return danmuVersion is not { } version || pushedVersion <= version;
	}

	private void SetTitle(string title, long? danmuVersion = null)
	{
		bool notify;
		SplitControl? split;

		lock (_lock)
		{
			if (_title == title || !IsCurrent(_titleVersion, danmuVersion))
			{
				return;
			}

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

	private void SetLiveStatus(LiveStatus status, long? danmuVersion = null)
	{
		string? title;
		bool notify;

		lock (_lock)
		{
			if (_liveStatus == status || !IsCurrent(_liveStatusVersion, danmuVersion))
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
			services.Notifications.Show(AppLanguage.Current.LiveStarted.ToString(StreamerName), title ?? string.Empty, link: LiveRoomUri);
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

	// 录制循环还在收尾时不开始仅录弹幕，免得两边同时写
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
		await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
		using IDisposable? scope = _logger.BeginRoomScope(RoomId);
		using IDisposable diskSpaceWatch = services.DiskSpaceMonitor.Watch();
		LiveRecordingSession liveSession = new(_logger);
		RecordingFaultTracker faults = new(services.TimeProvider);
		SetLastError(null);

		try
		{
			while (ShouldKeepRecording(cancellationToken))
			{
				SetConnecting();
				AppConfig config = await services.ConfigManager.GetAsync(cancellationToken);
				RecordingProfile profile = RecordingProfile.Create(config, Settings);

				try
				{
					RecordingAttempt attempt = await RecordAttemptAsync(config, profile, liveSession, cancellationToken);
					await HandleAttemptEndAsync(attempt, liveSession.CandidatePool, faults, config, profile, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception exception) when (cancellationToken.IsCancellationRequested)
				{
					// 停止时磁盘卡住，收尾会抛出写盘失败；这是要求的停止，不留错误提示
					LogStoppedWithError(_logger, exception);
					throw new OperationCanceledException(cancellationToken);
				}
				catch (Exception exception)
				{
					await HandleAttemptFailureAsync(exception, faults, config, profile, cancellationToken);
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
			OnRecordingLoopEnded();
		}
	}

	private void OnRecordingLoopEnded()
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

	private async Task<RecordingAttempt> RecordAttemptAsync(AppConfig config, RecordingProfile profile, LiveRecordingSession liveSession, CancellationToken cancellationToken)
	{
		SplitControl split = SetSplitControl(new SplitControl(() => RecordingSplitPolicy.Create(config, Settings), () => config.RemoveFillerData));

		try
		{
			return await services.Recorder.RecordAsync(RoomId, profile, liveSession, this, split, () => config.IncludeTitleInFileName ? Title : null, cancellationToken);
		}
		finally
		{
			// 之后的刷新和重连等待都不算在录制，最后一段的后处理和弹幕收尾也不等它们
			SetConnecting();

			if (TakeRecording() is (var path, var closed))
			{
				services.PostProcessor.Track(PostProcessAsync(path, closed, Danmaku.CompleteSegment(path)));
			}

			Danmaku.OnVideoEnded();
		}
	}

	private async Task HandleAttemptEndAsync(RecordingAttempt attempt, CandidatePool pool, RecordingFaultTracker faults, AppConfig config, RecordingProfile profile, CancellationToken cancellationToken)
	{
		// 流结束时尽快确认是否已下播，下播造成的断流不算线路故障
		bool isRefreshed = await TryRefreshAsync(cancellationToken);

		if (attempt.IsQuickFailure)
		{
			faults.OnQuickFailure(attempt.ActiveDuration);
		}
		else
		{
			if (attempt.HasDiskWorked)
			{
				faults.OnRecorded();
			}

			SetLastError(null);
		}

		if (!ShouldKeepRecording(cancellationToken))
		{
			LogRecordingEnded(_logger, attempt.End.Description);
			return;
		}

		if (attempt.End is RecordingEnd.WriteFailed { Exception: var writeFailure })
		{
			pool.OnAttemptEnded(attempt);
			ReportWriteFailure(faults, config, writeFailure);
			await WaitAfterFailureAsync(writeFailure, profile.StreamReconnectDelay, cancellationToken);
		}
		else if (attempt.IsQuickFailure)
		{
			LogAttemptFailed(attempt);
			SetLastError(AppLanguage.Current.RecordingEndpointFailed.ToString(attempt.Candidate.DisplayName, attempt.Endpoint.Host, attempt.End.DisplayText));

			// 连上之后等不到数据，而接口照常访问得到、仍是直播中：主播端停了推流
			if (isRefreshed && attempt.End is RecordingEnd.Idle)
			{
				faults.OnStreamerSilent();
			}

			if (pool.OnAttemptEnded(attempt) is AttemptVerdict.WaitBeforeReconnect)
			{
				LogAllEndpointsFailed(_logger, profile.StreamReconnectDelay);
				ReportStall(faults, config);
				await Task.Delay(profile.StreamReconnectDelay, services.TimeProvider, cancellationToken);
			}
		}
		else
		{
			pool.OnAttemptEnded(attempt);
			LogRecordingEnded(_logger, attempt.End.Description);
		}
	}

	private async Task HandleAttemptFailureAsync(Exception exception, RecordingFaultTracker faults, AppConfig config, RecordingProfile profile, CancellationToken cancellationToken)
	{
		// 拉不到流也可能是已经下播
		await TryRefreshAsync(cancellationToken);

		if (!ShouldKeepRecording(cancellationToken))
		{
			LogRecordingEnded(_logger, exception.Message);
			return;
		}

		switch (exception)
		{
			case RecordingWriteException writeFailure:
				ReportWriteFailure(faults, config, writeFailure);
				break;
			case StreamUnavailableException { IsStreamerSilent: true }:
				faults.OnStreamerSilent();
				break;
			default:
				ReportStall(faults, config);
				break;
		}

		await WaitAfterFailureAsync(exception, profile.StreamReconnectDelay, cancellationToken);
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
			services.Notifications.Show(AppLanguage.Current.RecordingWriteFailed.ToString(StreamerName), exception.DisplayText, AppNotificationType.Error);
		}
	}

	private async Task WaitAfterFailureAsync(Exception exception, TimeSpan delay, CancellationToken cancellationToken)
	{
		LogRecordingFailed(_logger, exception, delay);
		SetLastError(exception switch
		{
			RecordingWriteException writeFailure => writeFailure.DisplayText,
			StreamUnavailableException unavailable => unavailable.DisplayText,
			_ => BilibiliErrors.Describe(exception)
		});
		await Task.Delay(delay, services.TimeProvider, cancellationToken);
	}

	private async Task<bool> TryRefreshAsync(CancellationToken cancellationToken)
	{
		try
		{
			await RefreshAsync(cancellationToken);
			return true;
		}
		catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
		{
			LogRefreshFailed(_logger, exception);
			return false;
		}
		// 应答已经收到、还在校验时被取消，失败也按取消算；否则状态轮询会以失败结束，房间释放时跳过弹幕收尾
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			throw new OperationCanceledException(null, exception, cancellationToken);
		}
	}

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

	private async Task PostProcessAsync(string path, Task closed, Task<DanmakuSegment?> danmaku)
	{
		try
		{
			AppConfig config = await services.ConfigManager.GetAsync();
			RoomConfig room = Settings;
			RecordingProfile profile = RecordingProfile.Create(config, room);
			DanmakuProfile danmakuProfile = DanmakuProfile.Create(config, room);
			DanmakuSegment? segment;

			// 写盘端还开着它时，转封装读不到还没写出的末尾，按大小判断要不要删也不准
			try
			{
				await closed.WaitAsync(RecordingCloseTimeout, services.TimeProvider);
			}
			catch (TimeoutException)
			{
				LogRecordingCloseTimedOut(_logger, path);
				return;
			}

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
		catch (Exception exception)
		{
			// 这个任务只被登记等待，没有人读它的结果
			LogPostProcessFailed(_logger, exception, path);
		}
	}

	private (string Path, Task Closed)? TakeRecording()
	{
		lock (_lock)
		{
			if (_recordingPath is not { } path)
			{
				return null;
			}

			_recordingPath = null;
			return (path, _recordingClosed);
		}
	}

	[LoggerMessage(Level = LogLevel.Error, Message = "Failed to post-process {Path}")]
	private static partial void LogPostProcessFailed(ILogger logger, Exception exception, string path);

	[LoggerMessage(Level = LogLevel.Warning, Message = "{Path} is still being written, leaving it without post-processing")]
	private static partial void LogRecordingCloseTimedOut(ILogger logger, string path);

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

	[LoggerMessage(Level = LogLevel.Warning, Message = "{Stream} stream from {Host} failed after {Duration} of data")]
	private static partial void LogAttemptFaulted(ILogger logger, Exception exception, string stream, string host, TimeSpan duration);

	[LoggerMessage(Level = LogLevel.Warning, Message = "{Stream} stream from {Host} ended after {Duration} of data ({Reason})")]
	private static partial void LogAttemptEnded(ILogger logger, string stream, string host, TimeSpan duration, string reason);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Recording stopped before the file was finished")]
	private static partial void LogStoppedWithError(ILogger logger, Exception exception);

	[LoggerMessage(Level = LogLevel.Information, Message = "Recording stopped")]
	private static partial void LogRecordingStopped(ILogger logger);

	[LoggerMessage(Level = LogLevel.Error, Message = "Recording failed, retrying in {Delay}")]
	private static partial void LogRecordingFailed(ILogger logger, Exception exception, TimeSpan delay);
}
