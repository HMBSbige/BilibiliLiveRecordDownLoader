using System.Diagnostics;
using System.Threading.Channels;

namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

/// <summary>
/// 录制流程的分段、重连等事件和弹幕排进同一个队列，保证先后顺序
/// 不录弹幕也没有界面订阅时关掉解析，保持只读 cmd 的快速路径
/// </summary>
internal sealed partial class RoomDanmaku : IAsyncDisposable
{
	// 热门房间峰值约每秒 40 条消息，开了原始消息时翻倍；留出几分钟的余量
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

	private bool IsRecording
	{
		get
		{
			lock (_lock)
			{
				return _isRecording;
			}
		}
	}

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
				bool isRecording;

				// 关掉解析后同一帧里已经解析出的消息还会交过来，和 UpdateOptions 里的清空串行
				lock (_lock)
				{
					isRecording = _isRecording;

					if (isRecording || _subscribers > 0)
					{
						Feed.Add(message);
					}
				}

				if (isRecording)
				{
					EnqueueMessage(message);
				}

				break;
			}
			case DanmuMessage.Raw:
			{
				if (IsRecording)
				{
					EnqueueMessage(message);
				}

				break;
			}
			// 直播状态和房间信息由房间循环处理
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
		Enqueue(new DanmakuWork.VideoStarted(timeline, path, run, _timeProvider.GetTimestamp()));
	}

	public void OnSegmentStarted(string path)
	{
		Enqueue(new DanmakuWork.SegmentStarted(path, _timeProvider.GetTimestamp()));
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

	public async ValueTask DisposeAsync()
	{
		_queue.Writer.TryComplete();
		Feed.SetDisconnected(_timeProvider.GetUtcNow());

		try
		{
			await _writer.WaitAsync(TimeSpan.FromSeconds(30), _timeProvider);
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
		if (Interlocked.Increment(ref _queuedMessages) > QueueCapacity)
		{
			Interlocked.Decrement(ref _queuedMessages);

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
