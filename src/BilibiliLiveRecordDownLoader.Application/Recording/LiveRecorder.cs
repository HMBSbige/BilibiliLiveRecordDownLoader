using System.Text.Json;

namespace BilibiliLiveRecordDownLoader.Application.Recording;

internal sealed partial class LiveRecorder(BilibiliApiClient apiClient, IHttpClientFactory httpClientFactory, IAppConfigManager configManager, TimeProvider timeProvider, ILogger<LiveRecorder> logger)
{
	private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(1);

	internal Func<string, Stream> CreateFile { get; init; } = CreateRecordingFile;

	internal TimeSpan DrainTimeout { get; init; } = RecordingWriteBuffer.DrainTimeout;

	public async Task<RecordingAttempt> RecordAsync(long roomId, RecordingProfile profile, LiveRecordingSession liveSession, IRecordingObserver observer, SplitControl split, Func<string?> getFileTitle, CancellationToken cancellationToken)
	{
		AppConfig config = await configManager.GetAsync(cancellationToken);
		(LiveStreamSession session, LiveStreamCandidate candidate, CandidateBatch batch) = await OpenAsync(roomId, profile, config.Cookie is not null, liveSession.CandidatePool, cancellationToken);
		Uri endpoint = session.Source;

		try
		{
			RecordingStream stream = new(candidate.Format, candidate.Codec, new StreamQuality(candidate.Qn), QualityRestrictions.Evaluate(candidate, batch.Candidates, profile, batch.Request.HasCookie), batch.QualityNames);
			TimeSpan idleTimeout = GetIdleTimeout(profile.StreamIdleTimeout, session.MaxSilence);

			_ = CreateOnDisk(() => Directory.CreateDirectory(RecordingFileLayout.GetRoomDirectory(profile.RecordDirectory, roomId)));
			string path = Reserve(timeProvider.GetLocalNow());
			// 查询参数里有用户的 uid 和签名，只记路径
			LogStreamSelected(logger, stream.Format, stream.Codec, stream.Quality.Qn, candidate.Source, endpoint.Host, endpoint.AbsolutePath, path);

			if (liveSession.TryUpdateRestriction(stream.Restriction) && stream.Restriction is { } restriction)
			{
				LogQualityRestricted(logger, stream.Quality.Qn, restriction.Highest.Qn, Explain(restriction));
			}

			(RecordingEnd end, TimeSpan activeDuration) = await RecordToFileAsync(session, candidate, stream, path, Reserve, idleTimeout, observer, split, cancellationToken);
			return new RecordingAttempt(candidate, endpoint, stream.Restriction, activeDuration, end);
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
					return RecordingFileLayout.GetRecordingPath(stem, candidate.Format);
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

	private async Task<(LiveStreamSession Session, LiveStreamCandidate Candidate, CandidateBatch Batch)> OpenAsync(long roomId, RecordingProfile profile, bool hasCookie, CandidatePool pool, CancellationToken cancellationToken)
	{
		// FastestResponse 同时打开各个主机，失败回调可能并发
		Lock failureLock = new();
		(LiveStreamCandidate Candidate, Uri Uri, Exception Exception)? lastFailure = null;

		// 主播没在推流时 CDN 返回 404
		bool isEveryFailureNotFound = true;
		CandidateBatch? lastBatch = null;
		CandidateRequest request = CandidateRequest.Create(profile, hasCookie);
		HttpClient httpClient = httpClientFactory.CreateClient(HttpClientNames.BilibiliStream);

		await foreach ((CandidateBatch batch, LiveStreamCandidate candidate) in pool.GetCandidatesAsync(request, ct => RequestCandidatesAsync(roomId, profile, request, ct), cancellationToken))
		{
			// 换了一批说明旧签名地址全部打不开，结果只看新的一批：旧地址 403、新地址 404 是主播没在推流
			if (!ReferenceEquals(batch, lastBatch))
			{
				lastBatch = batch;
				lastFailure = null;
				isEveryFailureNotFound = true;
			}

			try
			{
				return (await LiveStreamReader.OpenAsync(httpClient, candidate, profile.HostSelection, profile.StreamConnectTimeout, logger, (uri, exception) => OnHostFailed(candidate, uri, exception), timeProvider, cancellationToken), candidate, batch);
			}
			catch (AggregateException) when (!cancellationToken.IsCancellationRequested)
			{
			}
		}

		if (lastFailure is not ({ } failed, { } host, { } error))
		{
			throw new StreamUnavailableException("No live stream endpoint is left to try.", AppLanguage.Current.RecordingNoStreamLeft, isStreamerSilent: false);
		}

		string endpointFailed = AppLanguage.Current.RecordingEndpointFailed.ToString(failed.DisplayName, host.Host, RecordingEnd.GetDisplayText(error));
		throw new StreamUnavailableException($"None of the live stream endpoints could be opened, the last one failed with {failed.Name} {host.Host}: {error.Message}", AppLanguage.Current.RecordingStreamsUnavailable.ToString(endpointFailed), isEveryFailureNotFound);

		void OnHostFailed(LiveStreamCandidate candidate, Uri uri, Exception exception)
		{
			pool.MarkFailed(candidate, uri, exception is HttpRequestException { StatusCode: HttpStatusCode.Forbidden });

			if (candidate is LiveStreamCandidate.Variant)
			{
				LogVariantUnavailable(logger, candidate.Name, uri.Host, exception.Message);
				return;
			}

			lock (failureLock)
			{
				lastFailure = (candidate, uri, exception);
				isEveryFailureNotFound &= exception is HttpRequestException { StatusCode: HttpStatusCode.NotFound };
			}

			LogEndpointFailed(logger, exception, candidate.Name, uri.Host);
		}
	}

	private async Task<CandidateBatch> RequestCandidatesAsync(long roomId, RecordingProfile profile, CandidateRequest request, CancellationToken cancellationToken)
	{
		LiveStreamListing listing = await apiClient.GetStreamCandidatesAsync(roomId, profile.Quality.Qn, cancellationToken);
		IReadOnlyList<LiveStreamCandidate.Listed> listed = CandidateOrder.Sort(listing.Candidates, profile);

		if (listed is [])
		{
			throw new StreamUnavailableException($"Room {roomId} has no usable stream.", AppLanguage.Current.RecordingNoStreamOffered, isStreamerSilent: true);
		}

		// 接口有低于所选画质的流就去主播放列表找：accept_qn 不一定列出高于原画的档位，不能凭它判断是否受限
		// 匿名时所选画质可能只有一种编码，它打不开时还能试其他编码的同一档；配了 Cookie 也可能已经失效，所以不看是否登录
		IReadOnlyList<LiveStreamCandidate.Variant> variants = profile.Quality != StreamQuality.Default && listed.Any(candidate => candidate.Qn < profile.Quality.Qn)
			? await GetVariantsAsync(roomId, profile, listed, cancellationToken)
			: [];

		if (variants is [])
		{
			return new CandidateBatch(listed, listing.QualityNames, request);
		}

		Dictionary<long, string> qualityNames = new(listing.QualityNames);

		foreach (LiveStreamCandidate.Variant variant in variants)
		{
			if (variant.QualityName is { } name)
			{
				qualityNames.TryAdd(variant.Qn, name);
			}
		}

		// 直播间列表按合并后的名称显示画质，变体的错误提示也用它，两边叫法才一致
		// 选出的变体可能比接口给的流高、低或同一画质，要合起来一起排
		IEnumerable<LiveStreamCandidate> candidates = listed.Concat<LiveStreamCandidate>(variants.Select(variant => variant with { QualityName = qualityNames.GetValueOrDefault(variant.Qn) }));
		return new CandidateBatch(CandidateOrder.Sort(candidates, profile), qualityNames, request);
	}

	private async Task<IReadOnlyList<LiveStreamCandidate.Variant>> GetVariantsAsync(long roomId, RecordingProfile profile, IReadOnlyList<LiveStreamCandidate.Listed> listed, CancellationToken cancellationToken)
	{
		IReadOnlyList<LiveStreamCandidate.Variant> fetched = await MasterPlaylistCandidates.FetchAsync(TryFetchAsync, profile.Quality, cancellationToken);
		IReadOnlyList<LiveStreamCandidate.Variant> variants = MasterPlaylistCandidates.Select(fetched, listed, profile);

		// 请求失败时已经记过原因
		if (variants is [] && fetched is not [])
		{
			long lowest = listed.Min(static candidate => candidate.Qn);
			LogMasterPlaylistAddsNothing(logger, lowest, profile.Quality.Qn);
		}

		return variants;

		async Task<IReadOnlyList<LiveStreamCandidate.Variant>> TryFetchAsync(long qn, CancellationToken ct)
		{
			try
			{
				return await apiClient.GetMasterPlaylistVariantsAsync(roomId, qn, ct);
			}
			catch (Exception exception) when (!ct.IsCancellationRequested)
			{
				if (IsRemoteFailure(exception))
				{
					LogMasterPlaylistUnavailable(logger, qn, exception.Message);
				}
				else
				{
					LogMasterPlaylistFailed(logger, exception, qn);
				}

				return [];
			}
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
			QualityRestriction.OtherCodecUnavailable other => string.Create(CultureInfo.InvariantCulture, $"the {other.Codec} stream offers quality {other.Quality.Qn} but could not be opened"),
			QualityRestriction.NotLoggedIn => "not logged in",
			QualityRestriction.LoginMayHaveExpired => "the login may have expired"
		};
	}

	internal static TimeSpan GetIdleTimeout(TimeSpan configured, TimeSpan? maxSilence)
	{
		return maxSilence + SampleInterval is { } silence && silence > configured ? silence : configured;
	}

	private async Task<(RecordingEnd End, TimeSpan ActiveDuration)> RecordToFileAsync(LiveStreamSession session, LiveStreamCandidate candidate, RecordingStream stream, string path, Func<DateTimeOffset, string> reservePath, TimeSpan idleTimeout, IRecordingObserver observer, SplitControl split, CancellationToken cancellationToken)
	{
		// 录制中途也可能打开分段，所以不分段时同样按容器解析
		MediaSplitter splitter = CreateSplitter(candidate);
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

	internal static MediaSplitter CreateSplitter(LiveStreamCandidate candidate)
	{
		return (candidate.Protocol, candidate.Format) switch
		{
			(StreamProtocol.HttpHls, StreamFormat.Fmp4) => new Fmp4HlsSplitter(),
			(StreamProtocol.HttpHls, _) => new TsHlsSplitter(),
			(StreamProtocol.HttpStream, _) => new FlvSplitter()
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

	[LoggerMessage(Level = LogLevel.Information, Message = "Recording {Format}/{Codec} at quality {Quality} offered by {Source} from {Host}{StreamPath} to {Path}")]
	private static partial void LogStreamSelected(ILogger logger, StreamFormat format, StreamCodec codec, long quality, string source, string host, string streamPath, string path);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Recording at quality {Quality} while {Highest} is available: {Reason}")]
	private static partial void LogQualityRestricted(ILogger logger, long quality, long highest, string reason);

	[LoggerMessage(Level = LogLevel.Warning, Message = "{Count} HLS segments from {Host} left the playlist before they could be downloaded")]
	private static partial void LogSegmentsMissed(ILogger logger, long count, string host);

	[LoggerMessage(Level = LogLevel.Information, Message = "Splitting the recording ({Reason}), continuing in {Path}")]
	private static partial void LogSplit(ILogger logger, SplitReason reason, string path);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to open {Stream} stream from {Host}")]
	private static partial void LogEndpointFailed(ILogger logger, Exception exception, string stream, string host);

	[LoggerMessage(Level = LogLevel.Information, Message = "The master playlist requested at quality {Quality} is unavailable: {Error}")]
	private static partial void LogMasterPlaylistUnavailable(ILogger logger, long quality, string error);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to read the master playlist requested at quality {Quality}")]
	private static partial void LogMasterPlaylistFailed(ILogger logger, Exception exception, long quality);

	[LoggerMessage(Level = LogLevel.Information, Message = "The master playlist offers no stream from quality {Lowest} to {Quality} besides those from getRoomPlayInfo")]
	private static partial void LogMasterPlaylistAddsNothing(ILogger logger, long lowest, long quality);

	[LoggerMessage(Level = LogLevel.Information, Message = "{Stream} is not available from {Host}: {Error}")]
	private static partial void LogVariantUnavailable(ILogger logger, string stream, string host, string error);
}
