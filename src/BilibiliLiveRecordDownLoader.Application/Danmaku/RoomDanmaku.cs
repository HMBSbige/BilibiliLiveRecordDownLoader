using System.Threading.Channels;

namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

/// <summary>
/// 一个房间的弹幕分发：弹幕循环收到的消息当场交给界面，录弹幕时排进队列交给唯一的写入者；录制流程的分段、重连等事件也排进同一个队列，保证先后顺序。
/// 解析开关在录弹幕或有界面订阅时打开，否则保持只读 cmd 的快速路径。
/// </summary>
internal sealed partial class RoomDanmaku : IAsyncDisposable
{
	// 等归属确定时写入者会停下几秒，热门房间峰值约每秒 40 条消息，开了原始消息时翻倍；留出几分钟的余量，磁盘卡住太久才丢
	internal const int QueueCapacity = 20000;

	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;
	private readonly Action _onChanged;
	private readonly Channel<DanmakuWork> _queue = Channel.CreateUnbounded<DanmakuWork>(new UnboundedChannelOptions { SingleReader = true });
	private readonly CancellationTokenSource _lifetime = new();
	private readonly DanmakuRecorder _recorder;
	private readonly Task _writer;
	private readonly Lock _lock = new();
	private int _queuedMessages;
	private long _droppedMessages;
	private int _subscribers;
	private bool _isRecording;
	private bool _isConnected;

	public RoomDanmaku(long roomId, TimeProvider timeProvider, ILogger logger, Action onChanged)
	{
		_timeProvider = timeProvider;
		_logger = logger;
		_onChanged = onChanged;
		Feed = new DanmakuLiveFeed(timeProvider);
		_recorder = new DanmakuRecorder(roomId, timeProvider, logger, Options, OnRecordingChanged);
		_writer = Task.Run(() => _recorder.RunAsync(_queue.Reader, OnDequeued, _lifetime.Token));
	}

	// 弹幕连接每收完一帧读一次，改了立即生效
	public DanmuReceiveOptions Options { get; } = new();

	public DanmakuLiveFeed Feed { get; }

	// 只计因为队列满了没写进文件的弹幕
	public long DroppedMessages => Interlocked.Read(ref _droppedMessages);

	public RoomDanmakuSnapshot Snapshot => Feed.Snapshot;

	public bool IsWriting(string path)
	{
		return _recorder.IsWriting(path);
	}

	public void OnConnecting()
	{
		Feed.SetConnecting();
	}

	// 弹幕循环的线程上调用，不能阻塞
	public void OnReceived(DanmuMessage message)
	{
		switch (message)
		{
			case DanmuMessage.Connected connected:
			{
				lock (_lock)
				{
					_isConnected = true;
				}

				Feed.SetConnected(connected.UserId);
				Enqueue(new DanmakuWork.Received(message));
				_onChanged();
				break;
			}
			case DanmuMessage.LoginNotice:
			{
				Feed.SetMasked();
				Enqueue(new DanmakuWork.Received(message));
				_onChanged();
				break;
			}
			case DanmuMessage.Comment or DanmuMessage.SuperChat or DanmuMessage.SuperChatDeleted or DanmuMessage.Gift or DanmuMessage.GuardBought:
			{
				Feed.Add(message);
				EnqueueMessage(message);
				break;
			}
			case DanmuMessage.Raw:
			{
				EnqueueMessage(message);
				break;
			}
		}
	}

	// 没连上就失败的也算断开，界面在等待重连时显示断线；只有连上过的才在文件里记断开
	public void OnDisconnected()
	{
		bool wasConnected;

		lock (_lock)
		{
			wasConnected = _isConnected;
			_isConnected = false;
		}

		DanmuReceiveTime time = new(_timeProvider.GetTimestamp(), _timeProvider.GetUtcNow());
		Feed.SetDisconnected(time.UtcTime);

		if (wasConnected)
		{
			Enqueue(new DanmakuWork.Disconnected(time));
		}

		_onChanged();
	}

	// 房间是否在录弹幕：开了录弹幕，并且录制循环在运行或者在仅录弹幕
	public void SetRecording(bool isRecording)
	{
		lock (_lock)
		{
			_isRecording = isRecording;
			UpdateOptions();
		}
	}

	public IDisposable Subscribe()
	{
		lock (_lock)
		{
			++_subscribers;
			UpdateOptions();
		}

		return new Subscription(this);
	}

	public void OnTitleChanged(string title)
	{
		Enqueue(new DanmakuWork.TitleChanged(title, _timeProvider.GetTimestamp()));
	}

	public void OnVideoStarted(IRecordingTimeline timeline, string path, DanmakuRun run)
	{
		Enqueue(new DanmakuWork.VideoStarted(timeline, path, run));
	}

	public void OnSegmentStarted(string path)
	{
		Enqueue(new DanmakuWork.SegmentStarted(path));
	}

	// 结果在这一段的弹幕文件写完关闭之后给出；这一段没录弹幕时为 null
	public Task<DanmakuSegment?> CompleteSegment(string path)
	{
		TaskCompletionSource<DanmakuSegment?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

		if (!_queue.Writer.TryWrite(new DanmakuWork.SegmentCompleted(path, completion)))
		{
			completion.TrySetResult(null);
		}

		return completion.Task;
	}

	public void OnVideoEnded()
	{
		Enqueue(new DanmakuWork.VideoEnded());
	}

	public void StartTextOnly(DanmakuRun run)
	{
		Enqueue(new DanmakuWork.TextOnlyStarted(run, _timeProvider.GetTimestamp()));
	}

	public void StopTextOnly()
	{
		Enqueue(new DanmakuWork.TextOnlyStopped());
	}

	public void OnSettingsChanged()
	{
		Enqueue(new DanmakuWork.SettingsChanged());
	}

	public void EndSession()
	{
		Enqueue(new DanmakuWork.SessionEnded());
	}

	// 先写完已经排队的内容再关闭文件
	public async ValueTask DisposeAsync()
	{
		_queue.Writer.TryComplete();
		Feed.SetDisconnected(_timeProvider.GetUtcNow());

		try
		{
			await _writer.WaitAsync(TimeSpan.FromSeconds(30));
		}
		catch (TimeoutException)
		{
			await _lifetime.CancelAsync();
			await _writer;
		}

		_lifetime.Dispose();
	}

	private void Unsubscribe()
	{
		lock (_lock)
		{
			--_subscribers;
			UpdateOptions();
		}
	}

	private void UpdateOptions()
	{
		bool includeChat = _isRecording || _subscribers > 0;

		if (Options.IncludeChat && !includeChat)
		{
			Feed.Clear();
		}

		Options.IncludeChat = includeChat;
	}

	private void EnqueueMessage(DanmuMessage message)
	{
		bool isRecording;

		lock (_lock)
		{
			isRecording = _isRecording;
		}

		if (!isRecording)
		{
			return;
		}

		if (Interlocked.Increment(ref _queuedMessages) > QueueCapacity)
		{
			Interlocked.Decrement(ref _queuedMessages);

			// 第一次和之后每满一批记一次，不刷屏
			if (Interlocked.Increment(ref _droppedMessages) % 1000 is 1)
			{
				LogQueueFull(_logger, DroppedMessages);
			}

			return;
		}

		Enqueue(new DanmakuWork.Received(message));
	}

	private void Enqueue(DanmakuWork work)
	{
		_queue.Writer.TryWrite(work);
	}

	private void OnDequeued(DanmakuWork work)
	{
		if (work is DanmakuWork.Received { Message: not (DanmuMessage.Connected or DanmuMessage.LoginNotice) })
		{
			Interlocked.Decrement(ref _queuedMessages);
		}
	}

	private void OnRecordingChanged(bool isRecording)
	{
		if (Feed.SetRecording(isRecording))
		{
			_onChanged();
		}
	}

	private sealed class Subscription(RoomDanmaku owner) : IDisposable
	{
		private int _disposed;

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _disposed, 1) is 0)
			{
				owner.Unsubscribe();
			}
		}
	}

	[LoggerMessage(Level = LogLevel.Warning, Message = "The danmaku file writer is falling behind, {Count} messages have been left out so far")]
	private static partial void LogQueueFull(ILogger logger, long count);
}
