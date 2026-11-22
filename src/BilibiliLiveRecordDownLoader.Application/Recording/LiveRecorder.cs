namespace BilibiliLiveRecordDownLoader.Application.Recording;

internal sealed partial class LiveRecorder(BilibiliApiClient apiClient, IHttpClientFactory httpClientFactory, FFmpegProvider ffmpegProvider, IAppConfigManager configManager, TimeProvider timeProvider, ILogger<LiveRecorder> logger)
{
	private static readonly Uri Referrer = new(AppConsts.LiveRoomUrlPrefix);
	private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(1);

	internal Func<string, Stream> CreateFile { get; init; } = CreateRecordingFile;

	internal TimeSpan DrainTimeout { get; init; } = RecordingWriteBuffer.DrainTimeout;

	public async Task<RecordingAttempt> RecordAsync(long roomId, RecordingProfile profile, LiveRecordingSession liveSession, IRecordingObserver observer, CancellationToken cancellationToken)
	{
		FFmpegRunner? ffmpeg = null;
		RecorderType recorderType = profile.RecorderType;

		if (recorderType is RecorderType.FFmpeg)
		{
			ffmpeg = await ffmpegProvider.GetRunnerAsync(cancellationToken);

			if (ffmpeg is null)
			{
				LogFFmpegFallback(logger);
				recorderType = RecorderType.BuiltIn;
			}
		}

		HttpClient httpClient = httpClientFactory.CreateClient(HttpClientNames.BilibiliStream);
		(LiveStreamSession session, LiveStreamCandidate candidate) = await OpenAsync(roomId, httpClient, profile, liveSession, cancellationToken);
		Uri endpoint = session.Source;

		try
		{
			if (ffmpeg is not null)
			{
				// 只用内置连接验证线路，录制交给 FFmpeg 重新拉流
				string userAgent = BilibiliRequestHandler.ResolveUserAgent((await configManager.GetAsync(cancellationToken)).UserAgent);
				await session.DisposeAsync();
				session = new FFmpegStreamSession(ffmpeg, FFmpegArguments.Record(endpoint, userAgent, Referrer, profile.StreamConnectTimeout), endpoint);
			}

			_ = CreateOnDisk(() => Directory.CreateDirectory(RecordingFileLayout.GetRoomDirectory(profile.RecordDirectory, roomId)));
			string path = ReservePath(liveSession, profile.RecordDirectory, roomId, candidate.Format, recorderType);
			LogStreamSelected(logger, candidate.Format, candidate.Codec, candidate.Qn, endpoint.Host, path);

			TransferResult result = await RecordToFileAsync(session, candidate, path, profile, observer, cancellationToken);
			return new RecordingAttempt(candidate, endpoint, result.ActiveDuration, result.End);
		}
		finally
		{
			await session.DisposeAsync();
		}
	}

	internal static Stream CreateRecordingFile(string path)
	{
		return new FileStream
		(
			path,
			new FileStreamOptions
			{
				Mode = FileMode.CreateNew,
				Access = FileAccess.Write,
				Share = FileShare.Read,
				Options = FileOptions.Asynchronous,
				BufferSize = 1 << 20
			}
		);
	}

	// 网络盘断开或没有权限时目录和文件都建不了，和写入失败一样换线路也没用
	private static T CreateOnDisk<T>(Func<T> create)
	{
		try
		{
			return create();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			throw new RecordingWriteException(exception);
		}
	}

	// 同一秒内重连会得到相同的时间戳，不同格式的文件转封装后又会落到同一个 .mp4
	private string ReservePath(LiveRecordingSession liveSession, string recordDirectory, long roomId, StreamFormat format, RecorderType recorderType)
	{
		DateTimeOffset startTime = timeProvider.GetLocalNow();

		for (int sequence = 0; ; ++sequence)
		{
			string stem = RecordingFileLayout.GetRecordingStem(recordDirectory, roomId, startTime, sequence);

			if (RecordingFileLayout.IsStemAvailable(stem, File.Exists) && liveSession.TryReserveStem(stem))
			{
				return RecordingFileLayout.GetRecordingPath(stem, format, recorderType);
			}
		}
	}

	private async Task<(LiveStreamSession Session, LiveStreamCandidate Candidate)> OpenAsync(long roomId, HttpClient httpClient, RecordingProfile profile, LiveRecordingSession liveSession, CancellationToken cancellationToken)
	{
		string? lastFailure = null;
		IReadOnlyList<LiveStreamCandidate> candidates = liveSession.Candidates ??= await apiClient.GetStreamCandidatesAsync(roomId, (long)profile.Quality, profile.CodecPriority, profile.FormatPriority, cancellationToken);

		foreach (LiveStreamCandidate candidate in liveSession.GetUntriedCandidates(candidates))
		{
			try
			{
				return (await LiveStreamReader.OpenAsync(httpClient, candidate, profile.HostSelection, profile.StreamConnectTimeout, (uri, exception) => OnHostFailed(candidate, uri, exception), cancellationToken), candidate);
			}
			catch (AggregateException) when (!cancellationToken.IsCancellationRequested)
			{
				// 每条线路的失败都已在回调里记录
			}
		}

		// 所有线路都试过一遍，下次等重连间隔后重新请求候选流
		liveSession.Reset();
		throw new HttpRequestException(lastFailure is null ? "No live stream endpoint is left to try." : $"None of the live stream endpoints could be opened, the last one failed with {lastFailure}");

		void OnHostFailed(LiveStreamCandidate candidate, Uri uri, Exception exception)
		{
			lastFailure = $"{candidate.Format}/{candidate.Codec} {uri.Host}: {exception.Message}";
			liveSession.MarkFailed(candidate, uri);
			LogEndpointFailed(logger, exception, candidate.Format, candidate.Codec, uri.Host);
		}
	}

	private async Task<TransferResult> RecordToFileAsync(LiveStreamSession session, LiveStreamCandidate candidate, string path, RecordingProfile profile, IRecordingObserver observer, CancellationToken cancellationToken)
	{
		// 文件由写缓冲关闭
		Stream file = CreateOnDisk(() => CreateFile(path));

		// 文件建好之后才开始计时，创建失败时不留下看门狗
		observer.OnRecordingStarted(path, candidate);
		TransferMeter meter = new();

		return await TransferAsync(ct => RecordingWriteBuffer.CopyAsync(session, file, meter, DrainTimeout, ct), meter.TakePending, profile.StreamIdleTimeout, observer, cancellationToken);
	}

	private async Task<TransferResult> TransferAsync(Func<CancellationToken, Task<StreamEndReason>> transfer, Func<long> takeTransferredBytes, TimeSpan idleTimeout, IRecordingObserver observer, CancellationToken cancellationToken)
	{
		using CancellationTokenSource transferCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		Task<TransferStatistics> watchdog = WatchAsync(takeTransferredBytes, idleTimeout, observer, transferCancellation);
		RecordingEnd end;
		TransferStatistics statistics;

		try
		{
			end = new RecordingEnd.Completed(await transfer(transferCancellation.Token));
		}
		catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
		{
			end = exception is RecordingWriteException writeFailure ? new RecordingEnd.WriteFailed(writeFailure) : new RecordingEnd.Faulted(exception);
		}
		finally
		{
			await transferCancellation.CancelAsync();
			statistics = await watchdog;
		}

		// 外部没有取消时，传输被取消只可能来自空闲超时；磁盘卡住时网络读取也会停下，这时写盘失败才是原因
		return new TransferResult(statistics.IsIdle && end is not RecordingEnd.WriteFailed ? new RecordingEnd.Idle(idleTimeout) : end, statistics.ActiveDuration);
	}

	private async Task<TransferStatistics> WatchAsync(Func<long> takeTransferredBytes, TimeSpan idleTimeout, IRecordingObserver observer, CancellationTokenSource transfer)
	{
		using PeriodicTimer timer = new(SampleInterval, timeProvider);
		long startTimestamp = timeProvider.GetTimestamp();
		long lastTimestamp = startTimestamp;
		long? lastDataTimestamp = null;
		TimeSpan idle = TimeSpan.Zero;
		bool isIdle = false;

		try
		{
			while (await timer.WaitForNextTickAsync(transfer.Token))
			{
				long timestamp = timeProvider.GetTimestamp();
				TimeSpan elapsed = timeProvider.GetElapsedTime(lastTimestamp, timestamp);
				lastTimestamp = timestamp;

				long bytes = takeTransferredBytes();
				observer.OnSpeedChanged(bytes / elapsed.TotalSeconds);

				if (bytes > 0)
				{
					idle = TimeSpan.Zero;
					lastDataTimestamp = timestamp;
				}
				else
				{
					idle += elapsed;
				}

				if (idle >= idleTimeout)
				{
					isIdle = true;
					await transfer.CancelAsync();
					break;
				}
			}
		}
		catch (OperationCanceledException)
		{
			// 传输结束时停止采样
		}
		finally
		{
			observer.OnSpeedChanged(0);
		}

		// 最后一次采样之后到达的数据
		if (!isIdle && takeTransferredBytes() > 0)
		{
			lastDataTimestamp = timeProvider.GetTimestamp();
		}

		return new TransferStatistics(isIdle, lastDataTimestamp is { } last ? timeProvider.GetElapsedTime(startTimestamp, last) : TimeSpan.Zero);
	}

	private readonly record struct TransferStatistics(bool IsIdle, TimeSpan ActiveDuration);

	private readonly record struct TransferResult(RecordingEnd End, TimeSpan ActiveDuration);

	[LoggerMessage(Level = LogLevel.Warning, Message = "FFmpeg is not available, falling back to the built-in recorder")]
	private static partial void LogFFmpegFallback(ILogger logger);

	[LoggerMessage(Level = LogLevel.Information, Message = "Recording {Format}/{Codec} at quality {Quality} from {Host} to {Path}")]
	private static partial void LogStreamSelected(ILogger logger, StreamFormat format, StreamCodec codec, long quality, string host, string path);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to open {Format}/{Codec} stream from {Host}")]
	private static partial void LogEndpointFailed(ILogger logger, Exception exception, StreamFormat format, StreamCodec codec, string host);
}
