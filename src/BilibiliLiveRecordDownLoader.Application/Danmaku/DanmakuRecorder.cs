using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;

namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

/// <summary>
/// 房间唯一的弹幕写入者；等着归属确定的弹幕会挡住后面的工作，所以“关闭段”标记之前的弹幕都已经写进了那一段
/// 弹幕内容和观众信息只写进文件，不写日志
/// </summary>
internal sealed partial class DanmakuRecorder(long roomId, TimeProvider timeProvider, ILogger logger, DanmuReceiveOptions options, Action<bool> onRecordingChanged)
{
	// 未登录通知紧跟在认证之后，等它再写连接记录，免得一次连接写两条
	internal static readonly TimeSpan LoginNoticeWait = TimeSpan.FromSeconds(2);

	private static readonly TimeSpan AheadCheckInterval = TimeSpan.FromSeconds(1);

	private const int CompletedCapacity = 32;

	private static readonly string Version = ThisAssembly.Info.InformationalVersion.Split('+')[0];

	// 开着的段按开始的先后排列；没录弹幕或者建不出文件的段为 null
	private readonly OrderedDictionary<string, Sink.Video?> _videoSinks = new(StringComparer.Ordinal);
	private readonly HashSet<string> _closedVideoPaths = new(StringComparer.Ordinal);
	private readonly Dictionary<string, DanmakuSegment?> _completed = new(StringComparer.Ordinal);
	private readonly Queue<string> _completedOrder = new();
	private readonly ConcurrentDictionary<string, byte> _openPaths = new(StringComparer.OrdinalIgnoreCase);
	private VideoRun? _video;
	private TextRun? _text;
	private RawDanmakuWriter? _raw;
	private string? _rawStem;

	// 原始消息文件还没记当前的连接，等这次工作改完状态后补记
	private bool _rawAwaitsConnection;

	private DanmuMessage.Connected? _connection;
	private bool _isMasked;
	private DanmuMessage.Connected? _pendingConnection;
	private long _pendingSince;
	private bool _isRecording;

	// 写入失败和写到一半卡住用真实文件造不稳定，测试换成自己的流
	internal Func<string, DanmakuFileHeader, CancellationToken, Task<DanmakuXmlWriter>> CreateWriterAsync { get; init; } = (path, header, cancellationToken) => DanmakuXmlWriter.CreateFileAsync(path, header, DanmakuFlushPolicy.Default, timeProvider, cancellationToken);

	internal Func<string, RawDanmakuWriter> OpenRawWriter { get; init; } = path => RawDanmakuWriter.Open(path, DanmakuFlushPolicy.Default, timeProvider);

	// 其他线程调用
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

	// 不按时间轴放置：录制已经结束，放置可能等不到结果
	private async Task WriteAwaitedConnectionAsync()
	{
		if (_pendingConnection is not { } connected)
		{
			return;
		}

		foreach (Sink sink in OpenSinks.Where(static sink => sink.AwaitsConnection).ToArray())
		{
			sink.AwaitsConnection = false;
			await WriteAsync(sink, new DanmakuRecord.Connection(FromFileTime(sink, TimeSpan.Zero), connected.IsAnonymous, false), CancellationToken.None);
		}

		if (_rawAwaitsConnection && _raw is not null)
		{
			_rawAwaitsConnection = false;
			await GuardRawAsync(() => _raw.WriteConnectedAsync(connected.ReceivedAt.UtcTime, connected.IsAnonymous, false, CancellationToken.None));
		}
	}

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

				// 可读通知异步送达，WhenAny 可能先看到到期；已经排进来的工作里可能有未登录通知，要先处理
				if (completed == readable || reader.TryPeek(out _))
				{
					continue;
				}
			}

			await FlushDueAsync(cancellationToken);
			UpdateRecording();
		}
	}

	private TimeSpan? NextDue()
	{
		TimeSpan? due = null;

		foreach (Sink sink in OpenSinks.Where(static sink => !sink.IsFailed))
		{
			due = Min(due, sink.Writer.FlushDueIn);
		}

		due = Min(due, _raw?.FlushDueIn);

		// 画面追上压着的记录时没有通知，只能隔一会儿看一次
		if (_videoSinks.Values.Any(static sink => sink is { IsFailed: false, Ahead.Count: > 0 }))
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

		foreach (Sink sink in OpenSinks.Where(static sink => !sink.IsFailed).ToArray())
		{
			if (sink is Sink.Video video)
			{
				await ReleaseAsync(video, null, cancellationToken);
			}

			await GuardAsync(sink, () => sink.Writer.FlushIfDueAsync(cancellationToken));
		}

		if (_raw is not null)
		{
			await GuardRawAsync(() => _raw.FlushIfDueAsync(cancellationToken));
		}
	}

	private async Task ProcessAsync(DanmakuWork work, CancellationToken cancellationToken)
	{
		// 原始的未登录通知排在解析出的之前；开播时开始记录常落在认证和通知之间，只开文件的工作也不急着写
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
					_text = new TextRun(started.Run);
					_text.Sink = await OpenTextSinkAsync(_text, settings, started.Timestamp, null, cancellationToken);
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

		await WriteRawConnectionAsync(cancellationToken);
		UpdateRecording();
	}

	// 开始视频或仅弹幕会先关掉正在写的文件，它们关闭前要写进连接记录
	private bool OnlyOpensFiles(DanmakuWork work)
	{
		return work switch
		{
			DanmakuWork.SegmentStarted => true,
			DanmakuWork.VideoStarted or DanmakuWork.TextOnlyStarted => !OpenSinks.Any(),
			DanmakuWork.Received or DanmakuWork.Disconnected or DanmakuWork.TitleChanged or DanmakuWork.SegmentCompleted or DanmakuWork.VideoEnded or DanmakuWork.TextOnlyStopped or DanmakuWork.SettingsChanged or DanmakuWork.SessionEnded => false
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

				// 原始的未登录通知紧随其后，这里不必记是否打码
				if (_raw is not null)
				{
					_rawAwaitsConnection = false;
					await GuardRawAsync(() => _raw.WriteConnectedAsync(connected.ReceivedAt.UtcTime, connected.IsAnonymous, null, cancellationToken));
				}

				break;
			}
			case DanmuMessage.LoginNotice notice:
			{
				if (_pendingConnection is not null)
				{
					await FlushConnectionAsync(true, cancellationToken);
				}
				else if (_connection is { } connection && !_isMasked)
				{
					// 先补记再改 _isMasked，放置时新建的文件按补记之前的状态开头
					if (await PlaceAsync(notice.ReceivedAt.Timestamp, cancellationToken) is ({ } sink, var time))
					{
						await WriteAsync(sink, new DanmakuRecord.Connection(time, connection.IsAnonymous, true), cancellationToken);
					}

					_isMasked = true;
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
				// 暂停只停记弹幕，连接和标题照常记，恢复后文件里的状态才前后一致
				if (await PlaceAsync(message.ReceivedAt.Timestamp, cancellationToken) is ({ IsPaused: false } sink, var time) && DanmakuConverter.ToRecord(message, time, sink.Profile) is { } record)
				{
					await WriteAsync(sink, record, cancellationToken);
				}

				break;
			}
			// 由房间循环处理，不会排进来
			case DanmuMessage.LiveStarted or DanmuMessage.LiveStopped or DanmuMessage.RoomChanged:
			{
				break;
			}
			default:
			{
				throw new UnreachableException();
			}
		}
	}

	private async Task FlushConnectionAsync(bool masked, CancellationToken cancellationToken)
	{
		if (_pendingConnection is not { } connected)
		{
			return;
		}

		// 放置之后才改 _connection：放置时新建的文件开头还是连接确定之前的状态，也不算在等连接记录
		_pendingConnection = null;

		// 打开的文件都在等它时，接收时刻早于所有文件，不必放置
		Sink[] awaiting = [.. OpenSinks.Where(static sink => sink.AwaitsConnection)];
		bool isRawOpen = _raw is not null;
		(Sink Sink, TimeSpan Time)? placed = awaiting.Length > 0 && OpenSinks.All(static sink => sink.AwaitsConnection) ? null : await PlaceAsync(connected.ReceivedAt.Timestamp, cancellationToken);
		_connection = connected;
		_isMasked = masked;

		// 放置时才打开的原始消息文件打开时还不算连着
		_rawAwaitsConnection |= !isRawOpen && _raw is not null;
		await WriteRawConnectionAsync(cancellationToken);

		// 跨了文件时前一个文件断开到它的末尾
		foreach (Sink sink in OpenSinks.Where(static sink => sink.GapStart is not null).ToArray())
		{
			await CloseGapAsync(sink, placed is ({ } target, var time) && ReferenceEquals(target, sink) ? time : null, cancellationToken);
		}

		foreach (Sink sink in awaiting)
		{
			sink.AwaitsConnection = false;

			if (placed is not ({ } target, _) || !ReferenceEquals(target, sink))
			{
				await WriteAsync(sink, new DanmakuRecord.Connection(FromFileTime(sink, TimeSpan.Zero), connected.IsAnonymous, masked), cancellationToken);
			}
		}

		if (placed is ({ } connectedSink, var connectedTime))
		{
			await WriteAsync(connectedSink, new DanmakuRecord.Connection(connectedTime, connected.IsAnonymous, masked), cancellationToken);
		}
	}

	private async Task DisconnectAsync(DanmuReceiveTime time, CancellationToken cancellationToken)
	{
		if (_connection is null)
		{
			return;
		}

		// 先放置再改状态：放置时新建的文件开头还连着
		(Sink Sink, TimeSpan Time)? placed = await PlaceAsync(time.Timestamp, cancellationToken);
		await WriteRawConnectionAsync(cancellationToken);

		if (_raw is not null)
		{
			await GuardRawAsync(() => _raw.WriteDisconnectedAsync(time.UtcTime, cancellationToken));
		}

		_connection = null;
		_isMasked = false;

		if (placed is ({ } sink, var position))
		{
			sink.GapStart ??= position;
		}
	}

	private async Task ChangeTitleAsync(string title, long timestamp, CancellationToken cancellationToken)
	{
		if (_text is { Sink: { } current } text)
		{
			DanmakuRunSettings settings = await text.Run.GetSettingsAsync(cancellationToken);

			if (settings.SplitOnTitleChange)
			{
				await CloseSinkAsync(current, timeProvider.GetElapsedTime(current.StartTimestamp, timestamp), null);
				text.Sink = await OpenTextSinkAsync(text, settings, timestamp, title, cancellationToken);
				return;
			}
		}

		if (await PlaceAsync(timestamp, cancellationToken) is ({ } sink, var time) && !string.Equals(sink.Title, title, StringComparison.Ordinal))
		{
			sink.Title = title;
			await WriteAsync(sink, new DanmakuRecord.TitleChange(time, title), cancellationToken);
		}
	}

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
			++video.LateCount;
			return null;
		}

		// 写盘端已经开始新的一段，段开始的通知还排在后面
		Sink.Video? sink = _videoSinks.TryGetValue(position.Path, out Sink.Video? known) ? known : await AddVideoSinkAsync(video, position.Path, cancellationToken);
		return sink is { IsFailed: false } ? (sink, position.Time) : null;
	}

	private async Task StartSegmentAsync(string path, CancellationToken cancellationToken)
	{
		if (_video is not { } video)
		{
			return;
		}

		if (!_videoSinks.ContainsKey(path) && !_closedVideoPaths.Contains(path))
		{
			await AddVideoSinkAsync(video, path, cancellationToken);
		}

		if (!video.Settings.Profile.IsEnabled)
		{
			await CloseRawAsync(true);
		}
	}

	// 没录弹幕的段也登记，又打开录弹幕之前不再为它建文件
	private async Task<Sink.Video?> AddVideoSinkAsync(VideoRun video, string path, CancellationToken cancellationToken)
	{
		Sink.Video? sink = video.Settings.Profile.IsEnabled ? await OpenVideoSinkAsync(video, path, cancellationToken) : null;

		// DeliverSpillAsync 按登记的先后找下一段，先登记
		_videoSinks[path] = sink;
		await DeliverSpillAsync();
		return sink;
	}

	private async Task<DanmakuSegment?> CompleteSegmentAsync(string path)
	{
		_closedVideoPaths.Add(path);

		if (_videoSinks.Remove(path, out Sink.Video? sink))
		{
			DanmakuSegment? segment = await CloseVideoSinkAsync(path, sink);
			await DeliverSpillAsync();
			return segment;
		}

		return _completed.GetValueOrDefault(path);
	}

	private async Task EndVideoAsync()
	{
		if (_video is not { } video)
		{
			return;
		}

		while (_videoSinks.Count > 0)
		{
			(string path, Sink.Video? sink) = _videoSinks.GetAt(0);
			_videoSinks.RemoveAt(0);
			await CloseVideoSinkAsync(path, sink);
			await DeliverSpillAsync();
		}

		if (video.LateCount > 0)
		{
			LogLateDanmaku(logger, video.LateCount);
		}

		if (video.Spill.Count > 0)
		{
			LogBeyondEnd(logger, video.Spill.Count);
		}

		_closedVideoPaths.Clear();
		_video = null;
	}

	private async Task<DanmakuSegment?> CloseVideoSinkAsync(string path, Sink.Video? sink)
	{
		DanmakuSegment? segment = sink is null ? null : await CloseSinkAsync(sink, sink.GetDuration(), sink.Run.Timeline.GetResolution(sink.VideoPath));
		Remember(path, segment);
		return segment;
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
		await CloseRawAsync(true);
		_rawStem = null;
	}

	// 关了录弹幕时文件只暂停、到段结束才关闭，后处理要拿到整段的时长
	private async Task ApplySettingsAsync(CancellationToken cancellationToken)
	{
		if (_video is not { } video)
		{
			return;
		}

		bool wasEnabled = video.Settings.Profile.IsEnabled;
		video.Settings = await video.Run.GetSettingsAsync(cancellationToken);
		bool isEnabled = video.Settings.Profile.IsEnabled;

		foreach ((string path, Sink.Video? sink) in _videoSinks.ToArray())
		{
			if (sink is not null)
			{
				sink.IsPaused = !isEnabled;
			}
			else if (isEnabled)
			{
				_videoSinks.Remove(path);
			}
		}

		if (!isEnabled)
		{
			await CloseRawAsync(true);
		}
		else if (!wasEnabled && _rawStem is { } stem)
		{
			await OpenRawAsync(stem, video.Settings.Profile.SaveRaw);
		}
	}

	private async Task<Sink.Video?> OpenVideoSinkAsync(VideoRun video, string videoPath, CancellationToken cancellationToken)
	{
		string stem = RecordingFileLayout.GetStem(videoPath);
		DanmakuRunSettings settings = video.Settings;
		DanmakuFileHeader header = video.Run.CreateHeader() with { RecordStartTime = timeProvider.GetLocalNow(), Offset = settings.Profile.Offset };
		return await OpenSinkAsync(stem, header, settings, (path, writer) => new Sink.Video(path, writer, settings.Profile, video, videoPath), cancellationToken);
	}

	// 只交给紧接着的一段，时间已经按它换算；它没有弹幕文件时丢掉，不能顺延到更后面的段
	private async Task DeliverSpillAsync()
	{
		if (_video is not { Spill.Count: > 0 } video || _videoSinks.Count is 0)
		{
			return;
		}

		if (_videoSinks.GetAt(0).Value is { } next)
		{
			// 比下一段里压着的记录都早，放在最前面
			next.Ahead.InsertRange(0, video.Spill);
			video.Spill.Clear();
			await ReleaseAsync(next, null, CancellationToken.None);
		}
		else
		{
			video.Spill.Clear();
		}
	}

	// start 是房间决定开始或换文件的时刻，处理到时可能已经过了一会儿；title 是换文件时的新标题
	private async Task<Sink.Text?> OpenTextSinkAsync(TextRun text, DanmakuRunSettings settings, long start, string? title, CancellationToken cancellationToken)
	{
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

		return await OpenSinkAsync(stem, header, settings, (path, writer) => new Sink.Text(path, writer, settings.Profile, start), cancellationToken);
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

	private async Task<TSink?> OpenSinkAsync<TSink>(string stem, DanmakuFileHeader header, DanmakuRunSettings settings, Func<string, DanmakuXmlWriter, TSink> create, CancellationToken cancellationToken) where TSink : Sink
	{
		string path = stem + RecordingFileLayout.DanmakuExtension;
		string fullPath = Path.GetFullPath(path);
		header = header with { Version = Version };
		DanmakuXmlWriter writer;

		// 文件一建出来就可能被改写偏移，先登记再建
		bool isRegistered = _openPaths.TryAdd(fullPath, 0);

		try
		{
			writer = await CreateWriterAsync(path, header, cancellationToken);
		}
		catch (Exception exception)
		{
			if (isRegistered)
			{
				_openPaths.TryRemove(fullPath, out _);
			}

			if (exception is not (IOException or UnauthorizedAccessException))
			{
				throw;
			}

			LogOpenFailed(logger, exception, path);
			return null;
		}

		LogFileStarted(logger, path);
		TSink sink = create(path, writer);
		sink.Title = header.Title;

		if (_pendingConnection is not null)
		{
			sink.AwaitsConnection = true;
		}
		else if (_connection is { } connection)
		{
			await WriteAsync(sink, new DanmakuRecord.Connection(FromFileTime(sink, TimeSpan.Zero), connection.IsAnonymous, _isMasked), cancellationToken);
		}
		else
		{
			sink.GapStart = FromFileTime(sink, TimeSpan.Zero);
		}

		await OpenRawAsync(stem, settings.Profile.SaveRaw);
		return sink;
	}

	private async Task OpenRawAsync(string stem, bool isEnabled)
	{
		if (!isEnabled)
		{
			await CloseRawAsync(true);
			return;
		}

		if (_raw is null)
		{
			string path = (_rawStem ??= stem) + RecordingFileLayout.RawDanmakuExtension;

			try
			{
				_raw = OpenRawWriter(path);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				LogOpenFailed(logger, exception, path);
			}

			// 弹幕连接通常早于开始记录
			_rawAwaitsConnection = _raw is not null && (_pendingConnection is not null || _connection is not null);
		}

		options.IncludeRaw = _raw is not null;
	}

	// 原始的未登录通知不一定在文件里，补记时写明是否打码
	private async Task WriteRawConnectionAsync(CancellationToken cancellationToken)
	{
		if (!_rawAwaitsConnection || _pendingConnection is not null || _connection is not { } connection)
		{
			return;
		}

		_rawAwaitsConnection = false;

		if (_raw is not null)
		{
			await GuardRawAsync(() => _raw.WriteConnectedAsync(connection.ReceivedAt.UtcTime, connection.IsAnonymous, _isMasked, cancellationToken));
		}
	}

	private async Task CloseRawAsync(bool logFailure)
	{
		options.IncludeRaw = false;
		_rawAwaitsConnection = false;

		if (_raw is not { } raw)
		{
			return;
		}

		_raw = null;

		try
		{
			await raw.DisposeAsync();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			if (logFailure)
			{
				LogRawWriteFailed(logger, exception);
			}
		}
	}

	private async Task<DanmakuSegment?> CloseSinkAsync(Sink sink, TimeSpan? duration, VideoResolution? resolution)
	{
		if (_text is not null && ReferenceEquals(sink, _text.Sink))
		{
			_text.Sink = null;
		}

		try
		{
			if (sink is Sink.Video video)
			{
				// 读不到时长时全部写出，剩下的都在这一段末尾之后，换算成下一段的时间
				TimeSpan end = duration ?? TimeSpan.MaxValue;
				await ReleaseAsync(video, end, CancellationToken.None);
				video.Run.Spill.AddRange(video.Ahead.Select(record => record.Shift(-end)));
				video.Ahead.Clear();
			}

			if (sink.GapStart is not null && !sink.IsFailed)
			{
				await CloseGapAsync(sink, duration is { } end ? FromFileTime(sink, end) : null, CancellationToken.None);

				if (sink is Sink.Video gapped)
				{
					await ReleaseAsync(gapped, TimeSpan.MaxValue, CancellationToken.None);
				}
			}

			await sink.Writer.DisposeAsync();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			if (!sink.IsFailed)
			{
				LogWriteFailed(logger, exception, sink.Path);
			}

			return null;
		}
		finally
		{
			// 最后几条写完、文件关上之后才能改写偏移
			_openPaths.TryRemove(Path.GetFullPath(sink.Path), out _);
		}

		LogFileFinished(logger, sink.Path, sink.Writer.WrittenCount);
		return new DanmakuSegment(sink.Path, resolution, duration);
	}

	// end 为 null 时到这个文件已知的末尾
	private async Task CloseGapAsync(Sink sink, TimeSpan? end, CancellationToken cancellationToken)
	{
		if (sink.GapStart is not { } start)
		{
			return;
		}

		sink.GapStart = null;
		TimeSpan finish = end ?? (sink is Sink.Video video && video.GetDuration() is { } duration && FromFileTime(sink, duration) is var last && last > sink.LastTime ? last : sink.LastTime);

		if (finish > start)
		{
			await WriteAsync(sink, new DanmakuRecord.Gap(start, finish), cancellationToken);
		}
	}

	// 写入者会加上偏移，画面的开头和末尾要先减去偏移，写进文件时才对得上
	private static TimeSpan FromFileTime(Sink sink, TimeSpan time)
	{
		return sink switch
		{
			Sink.Video video => time - video.Profile.Offset,
			Sink.Text => time
		};
	}

	// 跟着视频的位置从最后到达的画面往后推，可能推过这一段最后一帧，先压着等画面追上或段关闭
	private async Task WriteAsync(Sink sink, DanmakuRecord record, CancellationToken cancellationToken)
	{
		if (sink.IsFailed)
		{
			return;
		}

		switch (sink)
		{
			case Sink.Text:
			{
				await WriteNowAsync(sink, record, cancellationToken);
				break;
			}
			case Sink.Video video:
			{
				video.Ahead.Add(record);
				await ReleaseAsync(video, null, cancellationToken);
				break;
			}
			default:
			{
				throw new UnreachableException();
			}
		}
	}

	// end 为 null 时按这一段目前的时长，读不到时长时全部写出；按加上偏移的时间比，否则正偏移会把段尾的弹幕写到画面结束之后
	// 按接收顺序取前缀：时间轴重新对齐时位置可能比前一条早零点几秒，读的一方都会按时间排序
	private async Task ReleaseAsync(Sink.Video sink, TimeSpan? end, CancellationToken cancellationToken)
	{
		// 前一段还没关时它的末尾可能还要接过来，先不写；还没登记的是最新的一段
		if (sink.Ahead.Count is 0 || (end is null && _videoSinks.Values.TakeWhile(other => !ReferenceEquals(other, sink)).Any(static other => other is not null)))
		{
			return;
		}

		TimeSpan? limit = end ?? sink.GetDuration();
		int count = 0;

		while (count < sink.Ahead.Count && (limit is not { } value || sink.Ahead[count].Time + sink.Profile.Offset <= value))
		{
			++count;
		}

		List<DanmakuRecord> ready = sink.Ahead.GetRange(0, count);
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

	// 失败后不再写这个文件，但待写内容留着，关闭时从原位置重写，让文件尽量以 </i> 结尾
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
			await CloseRawAsync(false);
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
		bool isRecording = OpenSinks.Any(static sink => sink is { IsFailed: false, IsPaused: false });

		if (isRecording != _isRecording)
		{
			_isRecording = isRecording;
			onRecordingChanged(isRecording);
		}
	}

	private closed class Sink(string path, DanmakuXmlWriter writer, DanmakuProfile profile)
	{
		public string Path { get; } = path;

		public DanmakuXmlWriter Writer { get; } = writer;

		public DanmakuProfile Profile { get; } = profile;

		public string Title { get; set; } = string.Empty;

		// 弹幕连接从这个文件内时间起断开
		public TimeSpan? GapStart { get; set; }

		// 连接记录还在等未登录通知
		public bool AwaitsConnection { get; set; }

		public TimeSpan LastTime { get; set; }

		public bool IsFailed { get; set; }

		public bool IsPaused { get; set; }

		public sealed class Video(string path, DanmakuXmlWriter writer, DanmakuProfile profile, VideoRun run, string videoPath) : Sink(path, writer, profile)
		{
			public VideoRun Run { get; } = run;

			public string VideoPath { get; } = videoPath;

			// 超出这一段已经提交的画面、暂不写出的记录
			public List<DanmakuRecord> Ahead { get; } = [];

			public TimeSpan? GetDuration()
			{
				return Run.Timeline.GetDuration(VideoPath);
			}
		}

		public sealed class Text(string path, DanmakuXmlWriter writer, DanmakuProfile profile, long startTimestamp) : Sink(path, writer, profile)
		{
			public long StartTimestamp { get; } = startTimestamp;
		}
	}

	private sealed class VideoRun(IRecordingTimeline timeline, DanmakuRun run, DanmakuRunSettings settings)
	{
		public IRecordingTimeline Timeline { get; } = timeline;

		public DanmakuRun Run { get; } = run;

		public DanmakuRunSettings Settings { get; set; } = settings;

		// 超出上一段末尾的记录，已经换算成下一段的时间
		public List<DanmakuRecord> Spill { get; } = [];

		// 到达时所在段的文件已经关闭的弹幕数
		public long LateCount { get; set; }
	}

	private sealed class TextRun(DanmakuRun run)
	{
		public DanmakuRun Run { get; } = run;

		public Sink.Text? Sink { get; set; }
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
