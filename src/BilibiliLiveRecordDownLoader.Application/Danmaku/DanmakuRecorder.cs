using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;

namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

/// <summary>
/// 房间唯一的弹幕写入者：按顺序处理房间交来的工作，把弹幕放进跟着视频分段的文件或者仅弹幕的文件，并写原始消息。
/// 跟着视频时按时间轴放置，等着归属确定的弹幕会挡住后面的工作，所以“关闭段”标记之前的弹幕都已经写进了那一段。
/// 弹幕内容和观众信息只写进文件，不写日志。
/// </summary>
internal sealed partial class DanmakuRecorder(long roomId, TimeProvider timeProvider, ILogger logger, DanmuReceiveOptions options, Action<bool> onRecordingChanged)
{
	// 匿名连接的未登录通知紧跟在认证之后，连接记录等它一会儿再写，免得同一次连接写两条
	internal static readonly TimeSpan LoginNoticeWait = TimeSpan.FromSeconds(2);

	private static readonly TimeSpan AheadCheckInterval = TimeSpan.FromSeconds(1);

	// 晚到的段关闭请求要能取到结果，只留最近的一些
	private const int CompletedCapacity = 32;

	private static readonly string Version = ThisAssembly.Info.InformationalVersion.Split('+')[0];

	private readonly Dictionary<string, Sink?> _videoSinks = new(StringComparer.Ordinal);
	private readonly HashSet<string> _closedVideoPaths = new(StringComparer.Ordinal);
	private readonly Dictionary<string, DanmakuSegment?> _completed = new(StringComparer.Ordinal);
	private readonly Queue<string> _completedOrder = new();
	private readonly ConcurrentDictionary<string, byte> _openPaths = new(StringComparer.OrdinalIgnoreCase);
	private VideoRun? _video;
	private TextRun? _text;
	private RawDanmakuWriter? _raw;
	private string? _rawStem;
	private bool _isConnected;
	private long _userId;
	private bool _isMasked;
	private DanmuMessage.Connected? _pendingConnection;
	private long _pendingSince;
	private bool _isRecording;
	private long _lateCount;
	private long _beyondEndCount;
	private long _openedSinks;

	// 其他线程查询，改写偏移时不能动正在写的文件
	public bool IsWriting(string path)
	{
		return _openPaths.ContainsKey(Path.GetFullPath(path));
	}

	private IEnumerable<Sink> OpenSinks => _videoSinks.Values.OfType<Sink>().Concat(_text is { Sink: { } sink } ? [sink] : []);

	public async Task RunAsync(ChannelReader<DanmakuWork> reader, Action<DanmakuWork> onDequeued, CancellationToken cancellationToken)
	{
		try
		{
			while (await ReadAsync(reader, cancellationToken) is { } work)
			{
				onDequeued(work);

				try
				{
					await ProcessAsync(work, cancellationToken);
				}
				catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
				{
					LogWorkFailed(logger, exception, work.GetType().Name);
				}
				finally
				{
					if (work is DanmakuWork.SegmentCompleted { Completion: var completion })
					{
						completion.TrySetResult(null);
					}
				}
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// 房间释放
		}
		finally
		{
			while (reader.TryRead(out DanmakuWork? left))
			{
				if (left is DanmakuWork.SegmentCompleted { Completion: var completion })
				{
					completion.TrySetResult(null);
				}
			}

			await WriteAwaitedConnectionAsync();
			await EndVideoAsync();
			await StopTextAsync();
			await EndSessionAsync();
		}
	}

	// 没等到通知就结束了，等着连接记录的文件按未打码记下开始时的状态；不再按时间轴放置，录制已经结束，放置可能等不到结果
	private async Task WriteAwaitedConnectionAsync()
	{
		if (_pendingConnection is not { } connected)
		{
			return;
		}

		foreach (Sink sink in OpenSinks.Where(static sink => sink.AwaitsConnection).ToArray())
		{
			sink.AwaitsConnection = false;
			await WriteAsync(sink, new DanmakuRecord.Connection(TimeSpan.Zero, connected.IsAnonymous, false), CancellationToken.None);
		}
	}

	// 空闲时按最早到期的文件刷新，等未登录通知的连接到时写出
	private async Task<DanmakuWork?> ReadAsync(ChannelReader<DanmakuWork> reader, CancellationToken cancellationToken)
	{
		while (true)
		{
			if (reader.TryRead(out DanmakuWork? work))
			{
				return work;
			}

			TimeSpan? due = NextDue();

			if (due is null)
			{
				if (!await reader.WaitToReadAsync(cancellationToken))
				{
					return null;
				}

				continue;
			}

			if (due > TimeSpan.Zero)
			{
				using CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				Task<bool> readable = reader.WaitToReadAsync(wait.Token).AsTask();
				Task delay = Task.Delay(due.Value, timeProvider, wait.Token);
				Task completed = await Task.WhenAny(readable, delay);
				await wait.CancelAsync();
				cancellationToken.ThrowIfCancellationRequested();

				if (completed == readable && readable.IsCompletedSuccessfully && !readable.Result)
				{
					return null;
				}

				// 可读的通知是异步送达的，到期和新工作几乎同时时 WhenAny 可能先看到到期；
				// 已经排进来的工作要先处理，等着的未登录通知可能就在里面，否则连接记录会被当成超时写成未打码
				if (completed == readable || reader.TryPeek(out _))
				{
					continue;
				}
			}

			await FlushDueAsync(cancellationToken);
		}
	}

	private TimeSpan? NextDue()
	{
		TimeSpan? due = null;

		foreach (Sink sink in OpenSinks)
		{
			due = Min(due, sink.Writer.FlushDueIn);
		}

		due = Min(due, _raw?.FlushDueIn);

		// 压着的记录等画面追上，没有新工作时也隔一会儿看一次
		if (OpenSinks.Any(static sink => sink.Ahead.Count > 0))
		{
			due = Min(due, AheadCheckInterval);
		}

		if (_pendingConnection is not null)
		{
			TimeSpan remaining = LoginNoticeWait - timeProvider.GetElapsedTime(_pendingSince);
			due = Min(due, remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
		}

		return due;

		static TimeSpan? Min(TimeSpan? a, TimeSpan? b)
		{
			return (a, b) switch
			{
				({ } x, { } y) => x < y ? x : y,
				_ => a ?? b
			};
		}
	}

	private async Task FlushDueAsync(CancellationToken cancellationToken)
	{
		if (_pendingConnection is not null && timeProvider.GetElapsedTime(_pendingSince) >= LoginNoticeWait)
		{
			await FlushConnectionAsync(false, cancellationToken);
		}

		foreach (Sink sink in OpenSinks.ToArray())
		{
			await ReleaseAsync(sink, null, cancellationToken);
			await GuardAsync(sink, () => sink.Writer.FlushIfDueAsync(cancellationToken));
		}

		if (_raw is not null)
		{
			await GuardRawAsync(() => _raw.FlushIfDueAsync(cancellationToken));
		}
	}

	private async Task ProcessAsync(DanmakuWork work, CancellationToken cancellationToken)
	{
		// 原始的未登录通知排在解析出的通知之前，不能先把连接记录写成未打码；
		// 只开文件的工作也不用急着写，开播时房间开始记录往往正好落在认证和通知之间，新文件等连接记录确定后再补上
		if (_pendingConnection is not null && work is not DanmakuWork.Received { Message: DanmuMessage.LoginNotice or DanmuMessage.Raw } && !OnlyOpensFiles(work))
		{
			await FlushConnectionAsync(false, cancellationToken);
		}

		switch (work)
		{
			case DanmakuWork.Received { Message: var message }:
			{
				await ReceiveAsync(message, cancellationToken);
				break;
			}
			case DanmakuWork.Disconnected { Time: var time }:
			{
				await DisconnectAsync(time, cancellationToken);
				break;
			}
			case DanmakuWork.TitleChanged changed:
			{
				await ChangeTitleAsync(changed.Title, changed.Timestamp, cancellationToken);
				break;
			}
			case DanmakuWork.VideoStarted started:
			{
				await StopTextAsync();
				await EndVideoAsync();
				DanmakuRunSettings settings = await started.Run.GetSettingsAsync(cancellationToken);
				_video = new VideoRun(started.Timeline, started.Run, settings);
				await StartSegmentAsync(started.Path, cancellationToken);
				break;
			}
			case DanmakuWork.SegmentStarted { Path: var path }:
			{
				if (_video is { } video)
				{
					video.Settings = await video.Run.GetSettingsAsync(cancellationToken);
					await StartSegmentAsync(path, cancellationToken);
				}

				break;
			}
			case DanmakuWork.SegmentCompleted completed:
			{
				completed.Completion.TrySetResult(await CompleteSegmentAsync(completed.Path));
				break;
			}
			case DanmakuWork.VideoEnded:
			{
				await EndVideoAsync();
				break;
			}
			case DanmakuWork.TextOnlyStarted started:
			{
				await EndVideoAsync();
				await StopTextAsync();
				DanmakuRunSettings settings = await started.Run.GetSettingsAsync(cancellationToken);

				if (settings.Profile.IsEnabled)
				{
					_text = new TextRun(started.Run, settings);
					_text.Sink = await OpenTextSinkAsync(_text, started.Timestamp, null, cancellationToken);
				}

				break;
			}
			case DanmakuWork.TextOnlyStopped:
			{
				await StopTextAsync();
				break;
			}
			case DanmakuWork.SettingsChanged:
			{
				await ApplySettingsAsync(cancellationToken);
				break;
			}
			case DanmakuWork.SessionEnded:
			{
				await EndSessionAsync();
				break;
			}
			default:
			{
				throw new UnreachableException();
			}
		}

		UpdateRecording();
	}

	// 开始视频或仅弹幕时会先关掉正在写的文件，那些文件要在关闭前写进连接记录，这时不算只开文件
	private bool OnlyOpensFiles(DanmakuWork work)
	{
		return work switch
		{
			DanmakuWork.SegmentStarted => true,
			DanmakuWork.VideoStarted or DanmakuWork.TextOnlyStarted => !OpenSinks.Any(),
			_ => false
		};
	}

	private async Task ReceiveAsync(DanmuMessage message, CancellationToken cancellationToken)
	{
		switch (message)
		{
			case DanmuMessage.Connected connected:
			{
				_pendingConnection = connected;
				_pendingSince = timeProvider.GetTimestamp();

				if (_raw is not null)
				{
					await GuardRawAsync(() => _raw.WriteConnectedAsync(connected.ReceivedAt.UtcTime, connected.UserId, cancellationToken));
				}

				break;
			}
			case DanmuMessage.LoginNotice notice:
			{
				if (_pendingConnection is not null)
				{
					await FlushConnectionAsync(true, cancellationToken);
				}
				else if (_isConnected && !_isMasked)
				{
					// 通知来得太晚，连接记录已经写过，再补一条打码的
					_isMasked = true;
					await WriteAtAsync(notice.ReceivedAt.Timestamp, time => new DanmakuRecord.Connection(time, _userId is 0, true), cancellationToken);
				}

				break;
			}
			case DanmuMessage.Raw raw:
			{
				if (_raw is not null)
				{
					await GuardRawAsync(() => _raw.WriteAsync(raw.ReceivedAt.UtcTime, raw.Command, raw.Json.Span, cancellationToken));
				}

				break;
			}
			case DanmuMessage.Comment or DanmuMessage.SuperChat or DanmuMessage.SuperChatDeleted or DanmuMessage.Gift or DanmuMessage.GuardBought:
			{
				if (await PlaceAsync(message.ReceivedAt.Timestamp, cancellationToken) is ({ } sink, var time) && DanmakuConverter.ToRecord(message, time, sink.Profile) is { } record)
				{
					await WriteAsync(sink, record, cancellationToken);
				}

				break;
			}
		}
	}

	private async Task FlushConnectionAsync(bool masked, CancellationToken cancellationToken)
	{
		if (_pendingConnection is not { } connected)
		{
			return;
		}

		_pendingConnection = null;
		_isConnected = true;
		_userId = connected.UserId;
		_isMasked = masked;

		// 连接早于等着它的文件开始，在这些文件里记成开始时的状态；打开的文件都在等它时，接收时刻不属于任何文件，不必再放置
		Sink[] awaiting = [.. OpenSinks.Where(static sink => sink.AwaitsConnection)];
		(Sink Sink, TimeSpan Time)? placed = awaiting.Length > 0 && OpenSinks.All(static sink => sink.AwaitsConnection) ? null : await PlaceAsync(connected.ReceivedAt.Timestamp, cancellationToken);

		// 断开期间开始的文件从 0 起算断开，跨了文件时前一个文件断开到它的末尾
		foreach (Sink sink in OpenSinks.Where(static sink => sink.GapStart is not null).ToArray())
		{
			await CloseGapAsync(sink, placed is ({ } target, var time) && ReferenceEquals(target, sink) ? time : null, cancellationToken);
		}

		foreach (Sink sink in awaiting)
		{
			sink.AwaitsConnection = false;

			if (placed is not ({ } target, _) || !ReferenceEquals(target, sink))
			{
				await WriteAsync(sink, new DanmakuRecord.Connection(TimeSpan.Zero, connected.IsAnonymous, masked), cancellationToken);
			}
		}

		if (placed is ({ } connectedSink, var connectedTime))
		{
			await WriteAsync(connectedSink, new DanmakuRecord.Connection(connectedTime, connected.IsAnonymous, masked), cancellationToken);
		}
	}

	private async Task DisconnectAsync(DanmuReceiveTime time, CancellationToken cancellationToken)
	{
		if (!_isConnected)
		{
			return;
		}

		_isConnected = false;
		_isMasked = false;

		if (_raw is not null)
		{
			await GuardRawAsync(() => _raw.WriteDisconnectedAsync(time.UtcTime, cancellationToken));
		}

		if (await PlaceAsync(time.Timestamp, cancellationToken) is ({ } sink, var position))
		{
			sink.GapStart ??= position;
		}
	}

	private async Task ChangeTitleAsync(string title, long timestamp, CancellationToken cancellationToken)
	{
		if (_text is { } text && text.Sink is { } current)
		{
			text.Settings = await text.Run.GetSettingsAsync(cancellationToken);

			if (text.Settings.SplitOnTitleChange)
			{
				await CloseSinkAsync(current, timeProvider.GetElapsedTime(current.StartTimestamp, timestamp), null);
				text.Sink = await OpenTextSinkAsync(text, timestamp, title, cancellationToken);
				return;
			}
		}

		if (await PlaceAsync(timestamp, cancellationToken) is ({ } sink, var time) && !string.Equals(sink.Title, title, StringComparison.Ordinal))
		{
			sink.Title = title;
			await WriteAsync(sink, new DanmakuRecord.TitleChange(time, title), cancellationToken);
		}
	}

	// 接收时刻在哪个文件的什么位置；没有对应的文件时为 null
	private async Task<(Sink Sink, TimeSpan Time)?> PlaceAsync(long timestamp, CancellationToken cancellationToken)
	{
		if (_text is { Sink: { } textSink })
		{
			TimeSpan elapsed = timeProvider.GetElapsedTime(textSink.StartTimestamp, timestamp);
			return (textSink, elapsed > TimeSpan.Zero ? elapsed : TimeSpan.Zero);
		}

		if (_video is not { } video || await video.Timeline.PlaceAsync(timestamp, cancellationToken) is not { } position)
		{
			return null;
		}

		if (_closedVideoPaths.Contains(position.Path))
		{
			++_lateCount;
			return null;
		}

		// 写盘端已经开始新的一段，段开始的通知还排在后面
		if (!_videoSinks.TryGetValue(position.Path, out Sink? sink))
		{
			sink = video.Settings.Profile.IsEnabled ? await OpenVideoSinkAsync(video, position.Path, cancellationToken) : null;
			_videoSinks[position.Path] = sink;
		}

		return sink is { IsFailed: false } ? (sink, position.Time) : null;
	}

	private async Task StartSegmentAsync(string path, CancellationToken cancellationToken)
	{
		if (_video is { } video && !_videoSinks.ContainsKey(path) && !_closedVideoPaths.Contains(path))
		{
			_videoSinks[path] = video.Settings.Profile.IsEnabled ? await OpenVideoSinkAsync(video, path, cancellationToken) : null;
		}

		if (_video is { Settings.Profile.SaveRaw: true })
		{
			options.IncludeRaw = true;
		}
	}

	private async Task<DanmakuSegment?> CompleteSegmentAsync(string path)
	{
		_closedVideoPaths.Add(path);

		if (_videoSinks.Remove(path, out Sink? sink))
		{
			DanmakuSegment? segment = sink is null ? null : await CloseSinkAsync(sink, sink.Timeline?.GetDuration(path), sink.Timeline?.GetResolution(path));
			Remember(path, segment);
			await DeliverSpillAsync();
			return segment;
		}

		return _completed.GetValueOrDefault(path);
	}

	// 关闭剩下的段；最后一段没有关闭通知，录制尝试结束时时间轴已经关闭，不会再等
	private async Task EndVideoAsync()
	{
		if (_video is null)
		{
			return;
		}

		foreach ((string path, Sink? sink) in _videoSinks.ToArray())
		{
			_videoSinks.Remove(path);
			Remember(path, sink is null ? null : await CloseSinkAsync(sink, sink.Timeline?.GetDuration(path), sink.Timeline?.GetResolution(path)));
			await DeliverSpillAsync();
		}

		if (_lateCount > 0)
		{
			LogLateDanmaku(logger, _lateCount);
			_lateCount = 0;
		}

		// 最后一段之后没有画面了
		_beyondEndCount += _video.Spill.Count;

		if (_beyondEndCount > 0)
		{
			LogBeyondEnd(logger, _beyondEndCount);
			_beyondEndCount = 0;
		}

		_videoSinks.Clear();
		_closedVideoPaths.Clear();
		_video = null;
	}

	private async Task StopTextAsync()
	{
		if (_text is { Sink: { } sink })
		{
			await CloseSinkAsync(sink, timeProvider.GetElapsedTime(sink.StartTimestamp), null);
		}

		_text = null;
	}

	private async Task EndSessionAsync()
	{
		options.IncludeRaw = false;
		_rawStem = null;

		if (_raw is { } raw)
		{
			_raw = null;

			try
			{
				await raw.DisposeAsync();
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				LogRawWriteFailed(logger, exception);
			}
		}
	}

	// 中途关了录弹幕时关闭现有的文件；又打开时当前这一段从下一条弹幕起补建文件
	private async Task ApplySettingsAsync(CancellationToken cancellationToken)
	{
		if (_video is not { } video)
		{
			return;
		}

		video.Settings = await video.Run.GetSettingsAsync(cancellationToken);

		foreach ((string path, Sink? sink) in _videoSinks.ToArray())
		{
			if (!video.Settings.Profile.IsEnabled && sink is not null)
			{
				_videoSinks.Remove(path);
				Remember(path, await CloseSinkAsync(sink, sink.Timeline?.GetDuration(path), sink.Timeline?.GetResolution(path)));
				_closedVideoPaths.Add(path);
				_beyondEndCount += video.Spill.Count;
				video.Spill.Clear();
			}
			else if (video.Settings.Profile.IsEnabled && sink is null)
			{
				_videoSinks.Remove(path);
			}
		}
	}

	private async Task<Sink?> OpenVideoSinkAsync(VideoRun video, string videoPath, CancellationToken cancellationToken)
	{
		string stem = RecordingFileLayout.GetStem(videoPath);
		DanmakuFileHeader header = video.Run.CreateHeader() with { RecordStartTime = timeProvider.GetLocalNow(), Offset = video.Settings.Profile.Offset };
		Sink? sink = await OpenSinkAsync(stem, header, video.Settings, sink => (sink.Timeline, sink.VideoPath) = (video.Timeline, videoPath), cancellationToken);

		if (sink is not null)
		{
			_videoSinks[videoPath] = sink;
			await DeliverSpillAsync();
		}

		return sink;
	}

	// 上一段关闭时超出它末尾的记录接到下一段的开头：视频是连续的，下一段从上一段的末尾接着往下
	private async Task DeliverSpillAsync()
	{
		if (_video is not { Spill.Count: > 0 } video || _videoSinks.Values.OfType<Sink>().Where(static sink => !sink.IsFailed).MinBy(static sink => sink.Order) is not { } next)
		{
			return;
		}

		// 比下一段里压着的记录都早，放在最前面
		next.Ahead.InsertRange(0, video.Spill);
		video.Spill.Clear();
		await ReleaseAsync(next, null, CancellationToken.None);
	}

	// 仅弹幕：主干按开始记录的时刻和标题，和视频文件同一套命名和占用检查；时间从开始记录起按单调时钟算，不加偏移。
	// start 是房间决定开始或换文件的时刻，写入者处理到时可能已经过了一会儿；title 是换文件时的新标题
	private async Task<Sink?> OpenTextSinkAsync(TextRun text, long start, string? title, CancellationToken cancellationToken)
	{
		DanmakuRunSettings settings = text.Settings = await text.Run.GetSettingsAsync(cancellationToken);
		DateTimeOffset startTime = timeProvider.GetLocalNow() - timeProvider.GetElapsedTime(start);
		DanmakuFileHeader header = text.Run.CreateHeader();
		header = header with { Title = title ?? header.Title, RecordStartTime = startTime, Offset = TimeSpan.Zero };
		string stem;

		try
		{
			Directory.CreateDirectory(RecordingFileLayout.GetRoomDirectory(settings.RecordDirectory, roomId));
			stem = ReserveStem(settings.RecordDirectory, startTime, settings.IncludeTitleInFileName ? header.Title : null);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			LogOpenFailed(logger, exception, RecordingFileLayout.GetRoomDirectory(settings.RecordDirectory, roomId));
			return null;
		}

		return await OpenSinkAsync(stem, header, settings, sink => sink.StartTimestamp = start, cancellationToken);
	}

	private string ReserveStem(string recordDirectory, DateTimeOffset startTime, string? title)
	{
		for (int sequence = 0; ; ++sequence)
		{
			string stem = RecordingFileLayout.GetRecordingStem(recordDirectory, roomId, startTime, title, sequence);

			if (RecordingFileLayout.IsStemAvailable(stem, File.Exists))
			{
				return stem;
			}
		}
	}

	private async Task<Sink?> OpenSinkAsync(string stem, DanmakuFileHeader header, DanmakuRunSettings settings, Action<Sink> configure, CancellationToken cancellationToken)
	{
		string path = stem + RecordingFileLayout.DanmakuExtension;
		header = header with { Version = Version };
		DanmakuXmlWriter writer;

		try
		{
			writer = await DanmakuXmlWriter.CreateFileAsync(path, header, DanmakuFlushPolicy.Default, timeProvider, cancellationToken);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			LogOpenFailed(logger, exception, path);
			return null;
		}

		LogFileStarted(logger, path);
		_openPaths.TryAdd(Path.GetFullPath(path), 0);
		Sink sink = new(path, writer, settings.Profile) { Title = header.Title, Order = ++_openedSinks };
		configure(sink);

		// 文件开始时记下当前的连接状态；还没连上时从 0 开始算断开；刚连上、还不知道是否打码时等连接记录确定
		if (_pendingConnection is not null)
		{
			sink.AwaitsConnection = true;
		}
		else if (_isConnected)
		{
			await WriteAsync(sink, new DanmakuRecord.Connection(TimeSpan.Zero, _userId is 0, _isMasked), cancellationToken);
		}
		else
		{
			sink.GapStart = TimeSpan.Zero;
		}

		await OpenRawAsync(stem, settings.Profile.SaveRaw);
		return sink;
	}

	// 一场一个原始消息文件，以这一场第一个弹幕文件的主干命名
	private async Task OpenRawAsync(string stem, bool isEnabled)
	{
		options.IncludeRaw = isEnabled;

		if (!isEnabled)
		{
			await EndSessionAsync();
			return;
		}

		if (_raw is not null)
		{
			return;
		}

		string path = (_rawStem ??= stem) + RecordingFileLayout.RawDanmakuExtension;

		try
		{
			_raw = RawDanmakuWriter.Open(path, DanmakuFlushPolicy.Default, timeProvider);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			LogOpenFailed(logger, exception, path);
		}
	}

	private async Task<DanmakuSegment?> CloseSinkAsync(Sink sink, TimeSpan? duration, VideoResolution? resolution)
	{
		if (_text is not null && ReferenceEquals(sink, _text.Sink))
		{
			_text.Sink = null;
		}

		_openPaths.TryRemove(Path.GetFullPath(sink.Path), out _);

		try
		{
			// 跟着视频的文件以时长为准：没超出的写进去，超出的交给下一段
			if (sink.Timeline is not null)
			{
				await ReleaseAsync(sink, duration, CancellationToken.None);

				if (sink.Ahead.Count > 0)
				{
					TimeSpan end = duration ?? TimeSpan.Zero;

					if (_video is { } video && ReferenceEquals(video.Timeline, sink.Timeline))
					{
						video.Spill.AddRange(sink.Ahead.Select(record => record.Shift(-end)));
					}
					else
					{
						_beyondEndCount += sink.Ahead.Count;
					}

					sink.Ahead.Clear();
				}
			}

			if (sink.GapStart is not null && !sink.IsFailed)
			{
				await CloseGapAsync(sink, duration, CancellationToken.None);
				await ReleaseAsync(sink, TimeSpan.MaxValue, CancellationToken.None);
			}

			await sink.Writer.DisposeAsync();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			LogWriteFailed(logger, exception, sink.Path);
			return null;
		}

		LogFileFinished(logger, sink.Path, sink.Writer.WrittenCount);
		return new DanmakuSegment(sink.Path, resolution, duration);
	}

	// 断开的区间结束于 end；没有时结束于这个文件已知的末尾
	private async Task CloseGapAsync(Sink sink, TimeSpan? end, CancellationToken cancellationToken)
	{
		if (sink.GapStart is not { } start)
		{
			return;
		}

		sink.GapStart = null;
		TimeSpan finish = end ?? (sink.Timeline?.GetDuration(sink.VideoPath ?? string.Empty) is { } duration && duration > sink.LastTime ? duration : sink.LastTime);

		if (finish > start)
		{
			await WriteAsync(sink, new DanmakuRecord.Gap(start, finish), cancellationToken);
		}
	}

	private async Task WriteAtAsync(long timestamp, Func<TimeSpan, DanmakuRecord> create, CancellationToken cancellationToken)
	{
		if (await PlaceAsync(timestamp, cancellationToken) is ({ } sink, var time))
		{
			await WriteAsync(sink, create(time), cancellationToken);
		}
	}

	// 跟着视频的文件：位置是从最后到达的画面往后推的，段末尾的弹幕可能推过这一段最后一帧；先压着，等画面追上或者段关闭时再定
	private async Task WriteAsync(Sink sink, DanmakuRecord record, CancellationToken cancellationToken)
	{
		if (sink.IsFailed)
		{
			return;
		}

		if (sink.Timeline is null)
		{
			await WriteNowAsync(sink, record, cancellationToken);
			return;
		}

		sink.Ahead.Add(record);
		await ReleaseAsync(sink, null, cancellationToken);
	}

	// 写出不晚于 end 的记录，end 为空时按时间轴上这一段目前的时长；读不到时长（认不出容器）时全部写出。按接收顺序写：新画面到达时时间轴会重新对齐，位置可能比前一条早零点几秒，读的一方都会按时间排序
	private async Task ReleaseAsync(Sink sink, TimeSpan? end, CancellationToken cancellationToken)
	{
		// 前一段还没关时它的末尾可能还要接过来，这一段先不写，文件里的记录保持按时间排列
		if (sink.Ahead.Count is 0 || (end is null && sink.Timeline is not null && _videoSinks.Values.Any(other => other is not null && other.Order < sink.Order)))
		{
			return;
		}

		TimeSpan? limit = end ?? (sink.Timeline is { } timeline && sink.VideoPath is { } video ? timeline.GetDuration(video) : null);
		int count = 0;

		while (count < sink.Ahead.Count && (limit is not { } value || sink.Ahead[count].Time <= value))
		{
			++count;
		}

		DanmakuRecord[] ready = [.. sink.Ahead.Take(count)];
		sink.Ahead.RemoveRange(0, count);

		foreach (DanmakuRecord record in ready)
		{
			await WriteNowAsync(sink, record, cancellationToken);
		}
	}

	private async Task WriteNowAsync(Sink sink, DanmakuRecord record, CancellationToken cancellationToken)
	{
		if (sink.IsFailed)
		{
			return;
		}

		sink.LastTime = record.Time > sink.LastTime ? record.Time : sink.LastTime;
		await GuardAsync(sink, () => sink.Writer.WriteAsync(record, cancellationToken));
	}

	// 写不进去时这个文件就此作罢，录制和其他文件照常
	private async Task GuardAsync(Sink sink, Func<ValueTask> write)
	{
		try
		{
			await write();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			sink.IsFailed = true;
			LogWriteFailed(logger, exception, sink.Path);
		}
	}

	private async Task GuardRawAsync(Func<ValueTask> write)
	{
		try
		{
			await write();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			LogRawWriteFailed(logger, exception);
			RawDanmakuWriter? raw = _raw;
			_raw = null;

			if (raw is not null)
			{
				try
				{
					await raw.DisposeAsync();
				}
				catch (Exception disposeException) when (disposeException is IOException or UnauthorizedAccessException)
				{
					// 已经记过写入失败
				}
			}
		}
	}

	private void Remember(string path, DanmakuSegment? segment)
	{
		if (_completed.TryAdd(path, segment))
		{
			_completedOrder.Enqueue(path);

			if (_completedOrder.Count > CompletedCapacity)
			{
				_completed.Remove(_completedOrder.Dequeue());
			}
		}
	}

	private void UpdateRecording()
	{
		bool isRecording = OpenSinks.Any();

		if (isRecording != _isRecording)
		{
			_isRecording = isRecording;
			onRecordingChanged(isRecording);
		}
	}

	private sealed class Sink(string path, DanmakuXmlWriter writer, DanmakuProfile profile)
	{
		public string Path { get; } = path;

		public DanmakuXmlWriter Writer { get; } = writer;

		public DanmakuProfile Profile { get; } = profile;

		public IRecordingTimeline? Timeline { get; set; }

		public string? VideoPath { get; set; }

		// 仅弹幕的文件从这一刻起算
		public long StartTimestamp { get; set; }

		// 文件里最后的标题，同样的标题不重复记
		public string Title { get; set; } = string.Empty;

		// 弹幕连接从这个文件内时间起断开
		public TimeSpan? GapStart { get; set; }

		// 文件开始时连接刚建立，连接记录还在等未登录通知
		public bool AwaitsConnection { get; set; }

		public TimeSpan LastTime { get; set; }

		public bool IsFailed { get; set; }

		// 打开的先后
		public long Order { get; init; }

		// 超出这一段已经提交的画面、暂不写出的记录
		public List<DanmakuRecord> Ahead { get; } = [];
	}

	private sealed class VideoRun(IRecordingTimeline timeline, DanmakuRun run, DanmakuRunSettings settings)
	{
		public IRecordingTimeline Timeline { get; } = timeline;

		public DanmakuRun Run { get; } = run;

		public DanmakuRunSettings Settings { get; set; } = settings;

		// 上一段关闭时超出它末尾的记录，已经换算到下一段的时间
		public List<DanmakuRecord> Spill { get; } = [];
	}

	private sealed class TextRun(DanmakuRun run, DanmakuRunSettings settings)
	{
		public DanmakuRun Run { get; } = run;

		public DanmakuRunSettings Settings { get; set; } = settings;

		public Sink? Sink { get; set; }
	}

	[LoggerMessage(Level = LogLevel.Information, Message = "Recording danmaku to {Path}")]
	private static partial void LogFileStarted(ILogger logger, string path);

	[LoggerMessage(Level = LogLevel.Information, Message = "Finished {Path} with {Count} records")]
	private static partial void LogFileFinished(ILogger logger, string path, long count);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to create {Path}")]
	private static partial void LogOpenFailed(ILogger logger, Exception exception, string path);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to write {Path}, danmaku are no longer recorded to it")]
	private static partial void LogWriteFailed(ILogger logger, Exception exception, string path);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to write raw danmaku, they are no longer saved until the next file")]
	private static partial void LogRawWriteFailed(ILogger logger, Exception exception);

	[LoggerMessage(Level = LogLevel.Information, Message = "{Count} danmaku arrived after their file was closed and were left out")]
	private static partial void LogLateDanmaku(ILogger logger, long count);

	[LoggerMessage(Level = LogLevel.Information, Message = "{Count} danmaku arrived after the last frame of the recording and were left out")]
	private static partial void LogBeyondEnd(ILogger logger, long count);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to handle {Work}")]
	private static partial void LogWorkFailed(ILogger logger, Exception exception, string work);
}
