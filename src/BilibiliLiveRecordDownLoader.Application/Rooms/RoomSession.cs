namespace BilibiliLiveRecordDownLoader.Application.Rooms;

internal sealed partial class RoomSession(RoomConfig settings, RoomSessionServices services, Action onChanged) : IRecordingObserver, IAsyncDisposable
{
	private readonly Lock _lock = new();
	private readonly CancellationTokenSource _lifetime = new();
	private readonly ILogger _logger = services.Logger.WithRoomScope(settings.RoomId);
	private RoomConfig _settings = settings;
	private long _shortId;
	private string? _userName;
	private string? _title;
	private LiveStatus _liveStatus = LiveStatus.Unknown;
	private RecordStatus _recordStatus;
	private double _bytesPerSecond;
	private string? _recordingPath;
	private ActiveRecordingDto? _activeRecording;
	private string? _lastError;
	private CancellationTokenSource? _monitorCancellation;
	private Task _monitorTask = Task.CompletedTask;
	private CancellationTokenSource? _recordingCancellation;
	private Task _recordingTask = Task.CompletedTask;
	private bool _isRecordingLoopActive;
	private bool _disposed;

	public long RoomId { get; } = settings.RoomId;

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
			return new RoomSnapshot(_settings.ToDto(), _shortId, _userName, _title, _liveStatus, _recordStatus, _bytesPerSecond, _activeRecording, _lastError);
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
		}

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
			_userName = info.UserName;
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

		lock (_lock)
		{
			_monitorCancellation?.Dispose();
			_monitorCancellation = null;
		}

		_lifetime.Dispose();
	}

	void IRecordingObserver.OnRecordingStarted(string path, LiveStreamCandidate stream)
	{
		lock (_lock)
		{
			_recordingPath = path;
			_activeRecording = new ActiveRecordingDto(stream.Format, stream.Codec, stream.Qn, services.TimeProvider.GetUtcNow());
			_recordStatus = RecordStatus.Recording;
		}

		onChanged();
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

		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				await foreach (DanmuMessage message in services.DanmuClient.ReceiveAsync(RoomId, cancellationToken))
				{
					Handle(message);
				}

				// 取消时枚举正常结束，不算断线
				if (cancellationToken.IsCancellationRequested)
				{
					return;
				}

				LogDanmuDisconnected(_logger);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception exception)
			{
				LogDanmuFailed(_logger, exception);
			}

			try
			{
				await Task.Delay(Settings.DanmuReconnectDelay, services.TimeProvider, cancellationToken);
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

	private void Handle(DanmuMessage message)
	{
		(LiveStatus? status, string? title) = message switch
		{
			DanmuMessage.LiveStarted => (LiveStatus.Live, null),
			DanmuMessage.LiveStopped { IsRound: true } => (LiveStatus.Round, null),
			DanmuMessage.LiveStopped => (LiveStatus.Idle, null),
			DanmuMessage.RoomChanged changed => ((LiveStatus?)null, changed.Title)
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

	private void SetTitle(string title)
	{
		lock (_lock)
		{
			if (_title == title)
			{
				return;
			}

			_title = title;
		}

		LogTitleChanged(_logger, title);
		onChanged();
	}

	private void SetLiveStatus(LiveStatus status)
	{
		string? userName;
		string? title;
		bool notify;

		lock (_lock)
		{
			if (_liveStatus == status)
			{
				return;
			}

			_liveStatus = status;
			userName = _userName;
			title = _title;
			notify = _settings.IsNotificationEnabled;
		}

		LogLiveStatusChanged(_logger, status);
		onChanged();

		if (status is not LiveStatus.Live)
		{
			return;
		}

		if (notify)
		{
			string streamer = string.IsNullOrEmpty(userName) ? RoomId.ToString(CultureInfo.InvariantCulture) : userName;
			services.Notifications.Show(AppLanguage.Current.LiveStarted.ToString(streamer), title ?? string.Empty);
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
		SetLastError(null);

		try
		{
			while (ShouldKeepRecording(cancellationToken))
			{
				SetConnecting();
				RecordingProfile profile = RecordingProfile.Create(await services.ConfigManager.GetAsync(cancellationToken), Settings);

				try
				{
					RecordingAttempt attempt = await services.Recorder.RecordAsync(RoomId, profile, liveSession, this, cancellationToken);

					// 流结束时尽快确认是否已下播，下播造成的断流不算线路故障
					await TryRefreshAsync(cancellationToken);

					if (ShouldKeepRecording(cancellationToken) && attempt.End is RecordingEnd.WriteFailed { Exception: var writeFailure })
					{
						// 不标记线路失败也不换线；正常录制过一段时间后和断流一样回到首选流
						if (!attempt.IsQuickFailure)
						{
							liveSession.Reset();
						}

						await WaitAfterFailureAsync(writeFailure, profile.StreamReconnectDelay, cancellationToken);
					}
					else if (ShouldKeepRecording(cancellationToken) && attempt.IsQuickFailure)
					{
						// 立即换下一条线路，全部试过之后才会等待重连间隔
						liveSession.MarkFailed(attempt.Candidate, attempt.Endpoint);
						LogAttemptFailed(attempt);
						SetLastError($"{attempt.Candidate.Format}/{attempt.Candidate.Codec} {attempt.Endpoint.Host}: {attempt.End.Description}");

						if (!liveSession.HasUntriedEndpoint)
						{
							// 所有线路都试过一遍，等待之后重新请求候选流从头再试
							liveSession.Reset();
							LogAllEndpointsFailed(_logger, profile.StreamReconnectDelay);
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
					}

					await WaitAfterFailureAsync(exception, profile.StreamReconnectDelay, cancellationToken);
				}
				finally
				{
					if (TakeRecordingPath() is { } path)
					{
						_ = services.PostProcessor.ProcessAsync(path, profile);
					}
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
			}

			onChanged();

			// 循环退出的同时可能又收到了开播消息
			TryStartRecording();
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

	private string? TakeRecordingPath()
	{
		lock (_lock)
		{
			string? path = _recordingPath;
			_recordingPath = null;
			return path;
		}
	}

	[LoggerMessage(Level = LogLevel.Information, Message = "Danmu connection closed")]
	private static partial void LogDanmuDisconnected(ILogger logger);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Danmu connection failed")]
	private static partial void LogDanmuFailed(ILogger logger, Exception exception);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to refresh room status")]
	private static partial void LogRefreshFailed(ILogger logger, Exception exception);

	[LoggerMessage(Level = LogLevel.Information, Message = "Title: {Title}")]
	private static partial void LogTitleChanged(ILogger logger, string title);

	[LoggerMessage(Level = LogLevel.Information, Message = "Live status: {Status}")]
	private static partial void LogLiveStatusChanged(ILogger logger, LiveStatus status);

	private void LogAttemptFailed(RecordingAttempt attempt)
	{
		(StreamFormat format, StreamCodec codec, string host) = (attempt.Candidate.Format, attempt.Candidate.Codec, attempt.Endpoint.Host);

		if (attempt.End is RecordingEnd.Faulted { Exception: var exception })
		{
			LogAttemptFaulted(_logger, exception, format, codec, host, attempt.ActiveDuration);
		}
		else
		{
			LogAttemptEnded(_logger, format, codec, host, attempt.ActiveDuration, attempt.End.Description);
		}
	}

	[LoggerMessage(Level = LogLevel.Warning, Message = "Every stream failed, retrying in {Delay}")]
	private static partial void LogAllEndpointsFailed(ILogger logger, TimeSpan delay);

	[LoggerMessage(Level = LogLevel.Information, Message = "Recording ended: {Reason}")]
	private static partial void LogRecordingEnded(ILogger logger, string reason);

	[LoggerMessage(Level = LogLevel.Warning, Message = "{Format}/{Codec} stream from {Host} failed after {Duration} of data, switching to the next stream")]
	private static partial void LogAttemptFaulted(ILogger logger, Exception exception, StreamFormat format, StreamCodec codec, string host, TimeSpan duration);

	[LoggerMessage(Level = LogLevel.Warning, Message = "{Format}/{Codec} stream from {Host} ended after {Duration} of data ({Reason}), switching to the next stream")]
	private static partial void LogAttemptEnded(ILogger logger, StreamFormat format, StreamCodec codec, string host, TimeSpan duration, string reason);

	[LoggerMessage(Level = LogLevel.Information, Message = "Recording stopped")]
	private static partial void LogRecordingStopped(ILogger logger);

	[LoggerMessage(Level = LogLevel.Error, Message = "Recording failed, retrying in {Delay}")]
	private static partial void LogRecordingFailed(ILogger logger, Exception exception, TimeSpan delay);
}
