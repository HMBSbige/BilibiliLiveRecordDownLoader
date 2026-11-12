namespace BilibiliLiveRecordDownLoader.Application.Recording;

internal sealed partial class LiveRecorder(BilibiliApiClient apiClient, IHttpClientFactory httpClientFactory, FFmpegProvider ffmpegProvider, IAppConfigManager configManager, TimeProvider timeProvider, ILogger<LiveRecorder> logger)
{
	private static readonly Uri Referrer = new(AppConsts.LiveRoomUrlPrefix);
	private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(1);

	public async Task RecordAsync(long roomId, RecordingProfile profile, IRecordingObserver observer, CancellationToken cancellationToken)
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

		IReadOnlyList<LiveStreamCandidate> candidates = await apiClient.GetStreamCandidatesAsync(roomId, (long)profile.Quality, profile.CodecPriority, profile.FormatPriority, cancellationToken);
		HttpClient httpClient = httpClientFactory.CreateClient(HttpClientNames.BilibiliStream);
		(LiveStreamSession? session, LiveStreamCandidate candidate) = await OpenAsync(httpClient, candidates, profile, cancellationToken);

		try
		{
			Directory.CreateDirectory(RecordingFileLayout.GetRoomDirectory(profile.RecordDirectory, roomId));
			string path = CreateUniquePath(profile.RecordDirectory, roomId, candidate.Format, recorderType);
			LogRecordingStarted(logger, path, candidate.Format, candidate.Codec, session.Source);

			if (ffmpeg is null)
			{
				await RecordBuiltInAsync(session, path, profile, observer, cancellationToken);
			}
			else
			{
				// 只用内置连接验证线路，录制交给 FFmpeg 重新拉流
				Uri source = session.Source;
				await session.DisposeAsync();
				session = null;

				observer.OnRecordingStarted(path);
				await RecordWithFFmpegAsync(ffmpeg, source, path, profile, observer, cancellationToken);
			}
		}
		finally
		{
			if (session is not null)
			{
				await session.DisposeAsync();
			}
		}
	}

	// 同一秒内重连会得到相同的文件名
	private string CreateUniquePath(string recordDirectory, long roomId, StreamFormat format, RecorderType recorderType)
	{
		DateTimeOffset startTime = timeProvider.GetLocalNow();

		for (int sequence = 0; ; ++sequence)
		{
			string path = RecordingFileLayout.CreateRecordingPath(recordDirectory, roomId, startTime, format, recorderType, sequence);

			if (!File.Exists(path))
			{
				return path;
			}
		}
	}

	private async Task<(LiveStreamSession Session, LiveStreamCandidate Candidate)> OpenAsync(HttpClient httpClient, IReadOnlyList<LiveStreamCandidate> candidates, RecordingProfile profile, CancellationToken cancellationToken)
	{
		foreach (LiveStreamCandidate candidate in candidates)
		{
			using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(profile.StreamConnectTimeout);

			try
			{
				return (await LiveStreamReader.OpenAsync(httpClient, candidate, profile.HostSelection, timeout.Token), candidate);
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				LogCandidateTimedOut(logger, candidate.Format, candidate.Codec);
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				LogCandidateFailed(logger, exception, candidate.Format, candidate.Codec);
			}
		}

		throw new HttpRequestException("No live stream is reachable.");
	}

	private async Task RecordBuiltInAsync(LiveStreamSession session, string path, RecordingProfile profile, IRecordingObserver observer, CancellationToken cancellationToken)
	{
		await using FileStream file = new
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

		// 文件建好之后才开始计时，创建失败时不留下看门狗
		observer.OnRecordingStarted(path);
		TransferMeter meter = new();
		using CancellationTokenSource transfer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		Task<bool> watchdog = WatchAsync(meter.TakePending, profile.StreamIdleTimeout, observer, transfer);

		try
		{
			await session.CopyToAsync(file, meter, transfer.Token);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			// 外部没有取消时，只有空闲超时会中断传输
		}
		finally
		{
			await transfer.CancelAsync();

			if (await watchdog)
			{
				LogStreamIdle(logger, profile.StreamIdleTimeout);
			}
		}
	}

	private async Task RecordWithFFmpegAsync(FFmpegRunner ffmpeg, Uri source, string path, RecordingProfile profile, IRecordingObserver observer, CancellationToken cancellationToken)
	{
		AppConfig config = await configManager.GetAsync(cancellationToken);
		long recordedBytes = 0;
		using CancellationTokenSource transfer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		Task<bool> watchdog = WatchAsync(TakeFileGrowth, profile.StreamIdleTimeout, observer, transfer);

		try
		{
			string userAgent = BilibiliRequestHandler.ResolveUserAgent(config.UserAgent);
			await ffmpeg.RunAsync(FFmpegArguments.Record(source, userAgent, Referrer, profile.StreamConnectTimeout, path), null, transfer.Token);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			// 外部没有取消时，只有空闲超时会中断 FFmpeg
		}
		finally
		{
			await transfer.CancelAsync();

			if (await watchdog)
			{
				LogStreamIdle(logger, profile.StreamIdleTimeout);
			}
		}

		return;

		long TakeFileGrowth()
		{
			FileInfo file = new(path);
			long size = file.Exists ? file.Length : 0;
			long growth = Math.Max(size - recordedBytes, 0);
			recordedBytes = size;
			return growth;
		}
	}

	private async Task<bool> WatchAsync(Func<long> takeTransferredBytes, TimeSpan idleTimeout, IRecordingObserver observer, CancellationTokenSource transfer)
	{
		using PeriodicTimer timer = new(SampleInterval, timeProvider);
		long lastTimestamp = timeProvider.GetTimestamp();
		TimeSpan idle = TimeSpan.Zero;

		try
		{
			while (await timer.WaitForNextTickAsync(transfer.Token))
			{
				long timestamp = timeProvider.GetTimestamp();
				TimeSpan elapsed = timeProvider.GetElapsedTime(lastTimestamp, timestamp);
				lastTimestamp = timestamp;

				long bytes = takeTransferredBytes();
				observer.OnSpeedChanged(bytes / elapsed.TotalSeconds);
				idle = bytes > 0 ? TimeSpan.Zero : idle + elapsed;

				if (idle >= idleTimeout)
				{
					await transfer.CancelAsync();
					return true;
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

		return false;
	}

	[LoggerMessage(Level = LogLevel.Warning, Message = "FFmpeg is not available, falling back to the built-in recorder")]
	private static partial void LogFFmpegFallback(ILogger logger);

	[LoggerMessage(Level = LogLevel.Information, Message = "Recording {Format}/{Codec} from {Source} to {Path}")]
	private static partial void LogRecordingStarted(ILogger logger, string path, StreamFormat format, StreamCodec codec, Uri source);

	[LoggerMessage(Level = LogLevel.Information, Message = "Timed out connecting to {Format}/{Codec} stream")]
	private static partial void LogCandidateTimedOut(ILogger logger, StreamFormat format, StreamCodec codec);

	[LoggerMessage(Level = LogLevel.Information, Message = "Failed to connect to {Format}/{Codec} stream")]
	private static partial void LogCandidateFailed(ILogger logger, Exception exception, StreamFormat format, StreamCodec codec);

	[LoggerMessage(Level = LogLevel.Warning, Message = "No data received for {IdleTimeout}, reconnecting")]
	private static partial void LogStreamIdle(ILogger logger, TimeSpan idleTimeout);
}
