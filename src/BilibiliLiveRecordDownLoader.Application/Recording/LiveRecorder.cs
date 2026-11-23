using System.Text.Json;

namespace BilibiliLiveRecordDownLoader.Application.Recording;

internal sealed partial class LiveRecorder(BilibiliApiClient apiClient, IHttpClientFactory httpClientFactory, SwitchableWebProxy proxy, FFmpegProvider ffmpegProvider, IAppConfigManager configManager, TimeProvider timeProvider, ILogger<LiveRecorder> logger)
{
	private static readonly Uri Referrer = new(AppConsts.LiveRoomUrlPrefix);
	private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(1);

	internal Func<string, Stream> CreateFile { get; init; } = CreateRecordingFile;

	internal TimeSpan DrainTimeout { get; init; } = RecordingWriteBuffer.DrainTimeout;

	internal Func<FFmpegRunner, IReadOnlyList<string>, Uri, LiveStreamSession> CreateFFmpegSession { get; init; } = static (ffmpeg, arguments, source) => new FFmpegStreamSession(ffmpeg, arguments, source);

	public async Task<RecordingAttempt> RecordAsync(long roomId, long streamerUid, RecordingProfile profile, LiveRecordingSession liveSession, IRecordingObserver observer, SplitControl split, Func<string?> getFileTitle, CancellationToken cancellationToken)
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

		AppConfig config = await configManager.GetAsync(cancellationToken);
		(LiveStreamSession session, LiveStreamCandidate candidate, CandidateBatch batch) = await OpenAsync(roomId, streamerUid, profile, config.Cookie is not null, liveSession, cancellationToken);
		Uri endpoint = session.Source;

		try
		{
			RecordingStream stream = new(candidate.Format, candidate.Codec, candidate.Qn, QualityRestrictions.Evaluate(candidate, batch.Candidates, profile, batch.Request.HasCookie));
			// 换成 FFmpeg 之前取：FFmpeg 拉的是同一条流，数据同样按段到达
			TimeSpan idleTimeout = GetIdleTimeout(profile.StreamIdleTimeout, session.MaxSilence);

			if (ffmpeg is not null)
			{
				string userAgent = BilibiliRequestHandler.ResolveUserAgent(config.UserAgent);
				await session.DisposeAsync();
				session = CreateFFmpegSession(ffmpeg, FFmpegArguments.Record(endpoint, userAgent, Referrer, profile.StreamConnectTimeout, GetFillerRemoval(config, stream.Codec), GetFFmpegProxy(endpoint)), endpoint);
			}

			_ = CreateOnDisk(() => Directory.CreateDirectory(RecordingFileLayout.GetRoomDirectory(profile.RecordDirectory, roomId)));
			string path = Reserve(timeProvider.GetLocalNow());
			// 查询参数里有用户的 uid 和签名，只记路径
			LogStreamSelected(logger, stream.Format, stream.Codec, stream.Quality, endpoint.Host, endpoint.AbsolutePath, path);

			if (liveSession.TryUpdateRestriction(stream.Restriction) && stream.Restriction is { } restriction)
			{
				LogQualityRestricted(logger, stream.Quality, restriction.Highest, Explain(restriction));
			}

			(RecordingEnd end, TimeSpan activeDuration) = await RecordToFileAsync(session, candidate, stream, recorderType, path, Reserve, idleTimeout, observer, split, cancellationToken);
			return new RecordingAttempt(candidate, endpoint, activeDuration, end);
		}
		finally
		{
			if (session.MissedSegments is > 0 and var missed)
			{
				LogSegmentsMissed(logger, missed, endpoint.Host);
			}

			await session.DisposeAsync();
		}

		// 同一秒内重连会得到相同的时间戳，不同格式的文件转封装后又会落到同一个 .mp4
		string Reserve(DateTimeOffset startTime)
		{
			DateTimeOffset localTime = TimeZoneInfo.ConvertTime(startTime, timeProvider.LocalTimeZone);
			string? title = getFileTitle();

			for (int sequence = 0; ; ++sequence)
			{
				string stem = RecordingFileLayout.GetRecordingStem(profile.RecordDirectory, roomId, localTime, title, sequence);

				if (RecordingFileLayout.IsStemAvailable(stem, File.Exists) && liveSession.TryReserveStem(stem))
				{
					return RecordingFileLayout.GetRecordingPath(stem, candidate.Format, recorderType);
				}
			}
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

	private async Task<(LiveStreamSession Session, LiveStreamCandidate Candidate, CandidateBatch Batch)> OpenAsync(long roomId, long streamerUid, RecordingProfile profile, bool hasCookie, LiveRecordingSession liveSession, CancellationToken cancellationToken)
	{
		// FastestResponse 同时打开各个主机，失败回调可能并发
		Lock failureLock = new();
		(string Log, string Display)? lastFailure = null;

		// 主播没在推流时 CDN 返回 404
		bool isEveryFailureNotFound = true;
		CandidateRequest request = CandidateRequest.Create(profile, hasCookie);

		if (liveSession.Candidates is not { } batch || batch.Request != request)
		{
			batch = liveSession.Candidates = await GetCandidatesAsync(roomId, streamerUid, profile, request, cancellationToken);
		}

		HttpClient httpClient = httpClientFactory.CreateClient(HttpClientNames.BilibiliStream);

		foreach (LiveStreamCandidate candidate in liveSession.GetUntriedCandidates(batch.Candidates))
		{
			try
			{
				return (await LiveStreamReader.OpenAsync(httpClient, candidate, profile.HostSelection, profile.StreamConnectTimeout, (uri, exception) => OnHostFailed(candidate, uri, exception), timeProvider, cancellationToken), candidate, batch);
			}
			catch (AggregateException) when (!cancellationToken.IsCancellationRequested)
			{
			}
		}

		liveSession.Reset();

		if (lastFailure is not ({ } log, { } display))
		{
			throw new StreamUnavailableException("No live stream endpoint is left to try.", AppLanguage.Current.RecordingNoStreamLeft, false);
		}

		throw new StreamUnavailableException($"None of the live stream endpoints could be opened, the last one failed with {log}", AppLanguage.Current.RecordingStreamsUnavailable.ToString(display), isEveryFailureNotFound);

		void OnHostFailed(LiveStreamCandidate candidate, Uri uri, Exception exception)
		{
			liveSession.MarkFailed(candidate, uri);

			if (candidate is LiveStreamCandidate.Variant)
			{
				LogVariantUnavailable(logger, candidate.Name, uri.Host, exception.Message);
				return;
			}

			lock (failureLock)
			{
				lastFailure = ($"{candidate.Name} {uri.Host}: {exception.Message}", AppLanguage.Current.RecordingEndpointFailed.ToString(candidate.Name, uri.Host, RecordingEnd.GetDisplayText(exception)));
				isEveryFailureNotFound &= exception is HttpRequestException { StatusCode: HttpStatusCode.NotFound };
			}

			LogEndpointFailed(logger, exception, candidate.Name, uri.Host);
		}
	}

	private async Task<CandidateBatch> GetCandidatesAsync(long roomId, long streamerUid, RecordingProfile profile, CandidateRequest request, CancellationToken cancellationToken)
	{
		IReadOnlyList<LiveStreamCandidate.Listed> listed = MasterPlaylistCandidates.PreferTrueOriginal(await apiClient.GetStreamCandidatesAsync(roomId, profile.RequestQuality, profile.CodecPriority, profile.FormatPriority, cancellationToken), profile);

		if (listed.Count is 0)
		{
			throw new StreamUnavailableException($"Room {roomId} has no usable stream.", AppLanguage.Current.RecordingNoStreamOffered, true);
		}

		if (MasterPlaylistCandidates.GetQualities(listed, profile, request.HasCookie) is not { Length: > 0 } qualities)
		{
			return new CandidateBatch(listed, request);
		}

		return new CandidateBatch([.. await GetVariantsAsync(roomId, MasterPlaylistCandidates.GetMid(profile, streamerUid), qualities, profile.CodecPriority, cancellationToken), .. listed], request);
	}

	private async Task<IReadOnlyList<LiveStreamCandidate.Variant>> GetVariantsAsync(long roomId, long mid, long[] qualities, IReadOnlyList<StreamCodec> codecPriority, CancellationToken cancellationToken)
	{
		string wanted = string.Join(", ", qualities);

		try
		{
			IReadOnlyList<LiveStreamCandidate.Variant> variants = MasterPlaylistCandidates.Select(await apiClient.GetMasterPlaylistVariantsAsync(roomId, mid, cancellationToken), qualities, codecPriority);

			if (variants.Count is 0)
			{
				LogMasterPlaylistUnavailable(logger, wanted, "no variant of these qualities is listed");
			}

			return variants;
		}
		catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
		{
			if (IsRemoteFailure(exception))
			{
				LogMasterPlaylistUnavailable(logger, wanted, exception.Message);
			}
			else
			{
				LogMasterPlaylistFailed(logger, exception, wanted);
			}

			return [];
		}

		static bool IsRemoteFailure(Exception exception)
		{
			return exception is BilibiliApiException or HttpRequestException or IOException or FormatException or JsonException || RequestTimeout.IsTimedOut(exception);
		}
	}

	private static string Explain(QualityRestriction restriction)
	{
		return restriction switch
		{
			QualityRestriction.OtherCodec other => string.Create(CultureInfo.InvariantCulture, $"the {other.Codec} stream offers quality {other.Quality}"),
			QualityRestriction.OtherCodecUnavailable other => string.Create(CultureInfo.InvariantCulture, $"the {other.Codec} stream offers quality {other.Quality} but could not be opened"),
			QualityRestriction.NotLoggedIn => "not logged in",
			QualityRestriction.LoginMayHaveExpired => "the login may have expired"
		};
	}

	internal static TimeSpan GetIdleTimeout(TimeSpan configured, TimeSpan? maxSilence)
	{
		return maxSilence + SampleInterval is { } silence && silence > configured ? silence : configured;
	}

	private async Task<(RecordingEnd End, TimeSpan ActiveDuration)> RecordToFileAsync(LiveStreamSession session, LiveStreamCandidate candidate, RecordingStream stream, RecorderType recorderType, string path, Func<DateTimeOffset, string> reservePath, TimeSpan idleTimeout, IRecordingObserver observer, SplitControl split, CancellationToken cancellationToken)
	{
		// 录制中途也可能打开分段，所以不分段时同样按容器解析
		MediaSplitter splitter = CreateSplitter(candidate, recorderType);
		TransferMeter meter = new();
		AttemptGate gate = new();

		// 文件由写缓冲关闭，建好之后到交给写缓冲之前不能再有会出错的步骤
		Stream file = CreateOnDisk(() => CreateFile(path));
		RecordingTimeline timeline = new(path, timeProvider, idleTimeout);
		SegmentedOutput output = new(file, path, writer => new RecordingSegmenter(writer, splitter, split, timeProvider, logger, timeline), OpenNext, completed => gate.TryRun(() => observer.OnSegmentCompleted(completed)));
		observer.OnRecordingStarted(path, stream, timeline, output.Closed);

		return await TransferAsync(CopyAsync, meter, idleTimeout, observer, cancellationToken);

		async Task<StreamEndReason> CopyAsync(CancellationToken ct)
		{
			try
			{
				return await RecordingWriteBuffer.CopyAsync(session, output, meter, DrainTimeout, ct);
			}
			finally
			{
				gate.Close();

				timeline.Close();
			}
		}

		(string Path, Stream File) OpenNext(SegmentStart start)
		{
			if (gate.IsClosed)
			{
				throw new OperationCanceledException(AttemptGate.ClosedMessage);
			}

			string next = reservePath(start.StartTime);
			Stream nextFile = CreateOnDisk(() => CreateFile(next));

			// 观察者知道新文件之后时间轴才往里放弹幕；和关闭尝试在同一把锁里，关上之后不会再多出一段
			if (gate.TryRun(() =>
				{
					LogSplit(logger, start.Reason, next);
					observer.OnSegmentStarted(next);
					timeline.OpenSegment(start.Segment, next);
				}))
			{
				return (next, nextFile);
			}

			nextFile.Dispose();
			FileOperations.TryDelete(next, logger);
			throw new OperationCanceledException(AttemptGate.ClosedMessage);
		}
	}

	private Uri? GetFFmpegProxy(Uri endpoint)
	{
		if (proxy.IsBypassed(endpoint) || proxy.GetProxy(endpoint) is not { } address)
		{
			return null;
		}

		if (address is { IsAbsoluteUri: true, Scheme: "http" })
		{
			return address;
		}

		LogFFmpegProxyUnsupported(logger, address.Scheme, endpoint.Host);
		return null;
	}

	internal static FillerRemoval GetFillerRemoval(AppConfig config, StreamCodec codec)
	{
		return (config.RemoveFillerData, codec) switch
		{
			(false, _) => FillerRemoval.None,
			(true, StreamCodec.Avc) => FillerRemoval.Avc,
			(true, StreamCodec.Hevc) => FillerRemoval.Hevc,
			(true, StreamCodec.Av1) => FillerRemoval.None
		};
	}

	internal static MediaSplitter CreateSplitter(LiveStreamCandidate candidate, RecorderType recorderType)
	{
		return (recorderType, candidate.Protocol, candidate.Format) switch
		{
			(RecorderType.FFmpeg, _, _) => new TransportStreamSplitter(),
			(RecorderType.BuiltIn, StreamProtocol.HttpHls, StreamFormat.Fmp4) => new Fmp4HlsSplitter(),
			(RecorderType.BuiltIn, StreamProtocol.HttpHls, _) => new TsHlsSplitter(),
			(RecorderType.BuiltIn, StreamProtocol.HttpStream, _) => new FlvSplitter()
		};
	}

	private async Task<(RecordingEnd End, TimeSpan ActiveDuration)> TransferAsync(Func<CancellationToken, Task<StreamEndReason>> transfer, TransferMeter meter, TimeSpan idleTimeout, IRecordingObserver observer, CancellationToken cancellationToken)
	{
		using CancellationTokenSource transferCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		Task<(bool IsIdle, TimeSpan ActiveDuration)> watchdog = WatchAsync(meter, idleTimeout, observer, transferCancellation);
		RecordingEnd end;
		bool isIdle;
		TimeSpan activeDuration;

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
			(isIdle, activeDuration) = await watchdog;
		}

		// 磁盘卡住时网络读取也会停下而触发空闲超时，这时写盘失败才是原因
		return (isIdle && end is not RecordingEnd.WriteFailed ? new RecordingEnd.Idle(idleTimeout) : end, activeDuration);
	}

	private async Task<(bool IsIdle, TimeSpan ActiveDuration)> WatchAsync(TransferMeter meter, TimeSpan idleTimeout, IRecordingObserver observer, CancellationTokenSource transfer)
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

				long bytes = meter.TakePending();
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
		}
		finally
		{
			observer.OnSpeedChanged(0);
		}

		if (!isIdle && meter.TakePending() > 0)
		{
			lastDataTimestamp = timeProvider.GetTimestamp();
		}

		return (isIdle, lastDataTimestamp is { } last ? timeProvider.GetElapsedTime(startTimestamp, last) : TimeSpan.Zero);
	}

	// 排空超时后写盘端还在后台换文件，尝试结束之后它不能再通知观察者；检查和通知在同一把锁里
	private sealed class AttemptGate
	{
		public const string ClosedMessage = "The recording attempt has ended.";

		private readonly Lock _lock = new();
		private bool _isClosed;

		public bool IsClosed
		{
			get
			{
				lock (_lock)
				{
					return _isClosed;
				}
			}
		}

		public bool TryRun(Action action)
		{
			lock (_lock)
			{
				if (_isClosed)
				{
					return false;
				}

				action();
				return true;
			}
		}

		public void Close()
		{
			lock (_lock)
			{
				_isClosed = true;
			}
		}
	}

	[LoggerMessage(Level = LogLevel.Warning, Message = "FFmpeg is not available, falling back to the built-in recorder")]
	private static partial void LogFFmpegFallback(ILogger logger);

	[LoggerMessage(Level = LogLevel.Warning, Message = "FFmpeg cannot use a {Scheme} proxy, connecting to {Host} directly")]
	private static partial void LogFFmpegProxyUnsupported(ILogger logger, string scheme, string host);

	[LoggerMessage(Level = LogLevel.Information, Message = "Recording {Format}/{Codec} at quality {Quality} from {Host}{StreamPath} to {Path}")]
	private static partial void LogStreamSelected(ILogger logger, StreamFormat format, StreamCodec codec, long quality, string host, string streamPath, string path);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Recording at quality {Quality} while {Highest} is available: {Reason}")]
	private static partial void LogQualityRestricted(ILogger logger, long quality, long highest, string reason);

	[LoggerMessage(Level = LogLevel.Warning, Message = "{Count} HLS segments from {Host} left the playlist before they could be downloaded")]
	private static partial void LogSegmentsMissed(ILogger logger, long count, string host);

	[LoggerMessage(Level = LogLevel.Information, Message = "Splitting the recording ({Reason}), continuing in {Path}")]
	private static partial void LogSplit(ILogger logger, SplitReason reason, string path);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to open {Stream} stream from {Host}")]
	private static partial void LogEndpointFailed(ILogger logger, Exception exception, string stream, string host);

	[LoggerMessage(Level = LogLevel.Information, Message = "The master playlist offers no stream of quality {Qualities}: {Error}")]
	private static partial void LogMasterPlaylistUnavailable(ILogger logger, string qualities, string error);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to read the master playlist for quality {Qualities}")]
	private static partial void LogMasterPlaylistFailed(ILogger logger, Exception exception, string qualities);

	[LoggerMessage(Level = LogLevel.Information, Message = "{Stream} is not available from {Host}: {Error}")]
	private static partial void LogVariantUnavailable(ILogger logger, string stream, string host, string error);
}
