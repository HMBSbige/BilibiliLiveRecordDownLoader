using BilibiliLive.Danmu;
using BilibiliLiveRecordDownLoader.Application.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Recording;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Danmaku;

// 写入者和时间轴之间的约定：按队列顺序等每条弹幕的归属，“关闭段”标记之前的弹幕都写进那一段，之后才到的丢弃
public sealed class RoomDanmakuTest
{
	[Test]
	[Timeout(10_000)]
	public async Task SegmentIsClosedOnlyAfterTheDanmakuBeforeItArePlacedAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		ControlledTimeline timeline = new();
		await using RoomDanmaku danmaku = Create(directory, time);
		string video = directory.Combine("a.flv");
		danmaku.SetRecording(true);
		danmaku.OnReceived(Connected(time));
		danmaku.OnVideoStarted(timeline, video, Run(directory));

		TaskCompletionSource<TimelinePosition?> pending = timeline.Next();
		danmaku.OnReceived(Comment(time, "还在等"));
		Task<DanmakuSegment?> closed = danmaku.CompleteSegment(video);
		await Task.Delay(100, cancellationToken);
		bool closedEarly = closed.IsCompleted;
		pending.SetResult(new TimelinePosition(0, video, TimeSpan.FromSeconds(3)));
		DanmakuSegment? segment = await closed.WaitAsync(cancellationToken);

		// 关闭之后才到、按时间却属于这一段的弹幕不再写入
		timeline.Next().SetResult(new TimelinePosition(0, video, TimeSpan.FromSeconds(4)));
		danmaku.OnReceived(Comment(time, "晚到"));
		danmaku.OnVideoEnded();
		await danmaku.DisposeAsync();
		DanmakuFile file = await DanmakuXmlReader.ReadFileAsync(directory.Combine("a.xml"), cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(closedEarly).IsFalse();
			await Assert.That(segment).IsEqualTo(new DanmakuSegment(directory.Combine("a.xml"), new VideoResolution(1280, 720), TimeSpan.FromSeconds(30)));
			await Assert.That(file.Records.OfType<DanmakuRecord.Comment>().Select(static comment => comment.Text).Single()).IsEqualTo("还在等");
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task NextSegmentFileIsOpenedWhenItsDanmakuArriveBeforeTheStartNoticeAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		ControlledTimeline timeline = new();
		await using RoomDanmaku danmaku = Create(directory, time);
		string first = directory.Combine("a.flv");
		string second = directory.Combine("b.flv");
		danmaku.SetRecording(true);
		danmaku.OnVideoStarted(timeline, first, Run(directory));

		timeline.Next().SetResult(new TimelinePosition(1, second, TimeSpan.FromSeconds(1)));
		danmaku.OnReceived(Comment(time, "第二段"));
		Task<DanmakuSegment?> closed = danmaku.CompleteSegment(first);
		danmaku.OnSegmentStarted(second);
		await closed.WaitAsync(cancellationToken);
		Task<DanmakuSegment?> last = danmaku.CompleteSegment(second);
		danmaku.OnVideoEnded();
		await last.WaitAsync(cancellationToken);

		DanmakuFile file = await DanmakuXmlReader.ReadFileAsync(directory.Combine("b.xml"), cancellationToken);

		await Assert.That(file.Records.OfType<DanmakuRecord.Comment>().Single().Time).IsEqualTo(TimeSpan.FromSeconds(1));
	}

	// 段末尾推过最后一帧的弹幕接到下一段开头；最后一段之后没有画面，丢弃
	[Test]
	[Timeout(10_000)]
	public async Task DanmakuPastTheEndOfASegmentContinueInTheNextOneAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		ControlledTimeline timeline = new() { Duration = static path => path.EndsWith("a.flv", StringComparison.Ordinal) ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(5) };
		await using RoomDanmaku danmaku = Create(directory, time);
		string first = directory.Combine("a.flv");
		string second = directory.Combine("b.flv");
		danmaku.SetRecording(true);
		danmaku.OnVideoStarted(timeline, first, Run(directory));

		timeline.Next().SetResult(new TimelinePosition(0, first, TimeSpan.FromSeconds(9.5)));
		danmaku.OnReceived(Comment(time, "末尾之前"));
		timeline.Next().SetResult(new TimelinePosition(0, first, TimeSpan.FromSeconds(10.5)));
		danmaku.OnReceived(Comment(time, "推过末尾"));
		Task<DanmakuSegment?> closed = danmaku.CompleteSegment(first);
		danmaku.OnSegmentStarted(second);
		await closed.WaitAsync(cancellationToken);
		timeline.Next().SetResult(new TimelinePosition(1, second, TimeSpan.FromSeconds(6)));
		danmaku.OnReceived(Comment(time, "录制结束之后"));
		Task<DanmakuSegment?> last = danmaku.CompleteSegment(second);
		danmaku.OnVideoEnded();
		await last.WaitAsync(cancellationToken);

		DanmakuFile a = await DanmakuXmlReader.ReadFileAsync(directory.Combine("a.xml"), cancellationToken);
		DanmakuFile b = await DanmakuXmlReader.ReadFileAsync(directory.Combine("b.xml"), cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(string.Join(", ", a.Records.OfType<DanmakuRecord.Comment>().Select(static comment => $"{comment.Text}@{comment.Time.TotalSeconds}"))).IsEqualTo("末尾之前@9.5");
			await Assert.That(string.Join(", ", b.Records.OfType<DanmakuRecord.Comment>().Select(static comment => $"{comment.Text}@{comment.Time.TotalSeconds}"))).IsEqualTo("推过末尾@0.5");
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task SegmentWithoutDanmakuRecordingHasNoFileAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		await using RoomDanmaku danmaku = Create(directory, time);
		string video = directory.Combine("a.flv");
		danmaku.OnVideoStarted(new ControlledTimeline(), video, Run(directory, isEnabled: false));

		DanmakuSegment? segment = await danmaku.CompleteSegment(video).WaitAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(segment).IsNull();
			await Assert.That(File.Exists(directory.Combine("a.xml"))).IsFalse();
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task MessagesBeyondTheQueueLimitAreCountedAsDroppedAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		ControlledTimeline timeline = new();
		await using RoomDanmaku danmaku = Create(directory, time);
		string video = directory.Combine("a.flv");
		danmaku.SetRecording(true);
		danmaku.OnVideoStarted(timeline, video, Run(directory));

		// 写入者卡在第一条的归属上
		TaskCompletionSource<TimelinePosition?> stuck = timeline.Next();
		danmaku.OnReceived(Comment(time, "第一条"));
		await timeline.Placing.Task.WaitAsync(cancellationToken);

		for (int i = 0; i < RoomDanmaku.QueueCapacity + 5; ++i)
		{
			danmaku.OnReceived(Comment(time, "刷屏"));
		}

		long dropped = danmaku.DroppedMessages;
		timeline.Fallback = new TimelinePosition(0, video, TimeSpan.Zero);
		stuck.SetResult(null);
		await danmaku.CompleteSegment(video).WaitAsync(cancellationToken);

		await Assert.That(dropped).IsEqualTo(5);
	}

	// 开播时房间开始记录常常落在认证和未登录通知之间，新文件只记一条打码的连接
	[Test]
	[Timeout(10_000)]
	public async Task AFileStartedBeforeTheLoginNoticeGetsOneMaskedConnectionAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		await using RoomDanmaku danmaku = Create(directory, time);
		danmaku.SetRecording(true);
		danmaku.OnReceived(Connected(time));
		danmaku.StartTextOnly(Run(directory));
		danmaku.OnReceived(new DanmuMessage.LoginNotice("未登录") { ReceivedAt = new DanmuReceiveTime(time.GetTimestamp(), time.GetUtcNow()) });
		danmaku.OnReceived(Comment(time, "通知之后"));
		danmaku.StopTextOnly();
		await danmaku.DisposeAsync();

		DanmakuFile file = await DanmakuXmlReader.ReadFileAsync(Directory.GetFiles(directory.Root, "*.xml", SearchOption.AllDirectories).Single(), cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(file.Records.Count).IsEqualTo(2);
			await Assert.That(file.Records[0]).IsEqualTo(new DanmakuRecord.Connection(TimeSpan.Zero, true, true));
			await Assert.That(file.Records[1] is DanmakuRecord.Comment { Text: "通知之后" }).IsTrue();
		}
	}

	// 队列的可读通知是异步送达的：通知已经排进队列、可读通知还没送到时等待先到期，排进来的通知也要先处理，不能当成超时
	[Test]
	[Timeout(10_000)]
	public async Task ALoginNoticeQueuedBeforeTheDeadlineCountsEvenIfTheWaitEndsFirstAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		TimerWatchingTimeProvider watched = new(time);
		ManualReader reader = new();
		DanmakuRecorder recorder = new(1, watched, NullLogger.Instance, new DanmuReceiveOptions(), static _ => { });
		reader.Add(new DanmakuWork.TextOnlyStarted(Run(directory), time.GetTimestamp()));
		reader.Add(new DanmakuWork.Received(Connected(time)));
		Task running = recorder.RunAsync(reader, static _ => { }, cancellationToken);

		// 写入者开始等通知：可读通知不会来，只有到期能把它叫醒
		await watched.TimerCreated.Task.WaitAsync(cancellationToken);
		reader.Add(new DanmakuWork.Received(new DanmuMessage.LoginNotice("未登录") { ReceivedAt = new DanmuReceiveTime(time.GetTimestamp(), time.GetUtcNow()) }));
		time.Advance(DanmakuRecorder.LoginNoticeWait * 2);
		reader.Add(new DanmakuWork.TextOnlyStopped());
		reader.Complete();
		await running.WaitAsync(cancellationToken);

		DanmakuFile file = await DanmakuXmlReader.ReadFileAsync(Directory.GetFiles(directory.Root, "*.xml", SearchOption.AllDirectories).Single(), cancellationToken);

		await Assert.That(file.Records.OfType<DanmakuRecord.Connection>().ToArray()).IsEquivalentTo([new DanmakuRecord.Connection(TimeSpan.Zero, true, true)], EqualityComparer<DanmakuRecord.Connection>.Default);
	}

	// 没等到通知就有弹幕时照常按未打码记下连接，文件开头不会少了连接记录、也不会记成断开
	[Test]
	[Timeout(10_000)]
	public async Task AFileStartedBeforeTheConnectionIsSettledStillStartsConnectedAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		await using RoomDanmaku danmaku = Create(directory, time);
		danmaku.SetRecording(true);
		danmaku.OnReceived(Connected(time));
		danmaku.StartTextOnly(Run(directory));
		danmaku.OnReceived(Comment(time, "没有通知"));
		danmaku.StopTextOnly();
		await danmaku.DisposeAsync();

		DanmakuFile file = await DanmakuXmlReader.ReadFileAsync(Directory.GetFiles(directory.Root, "*.xml", SearchOption.AllDirectories).Single(), cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(file.Records.Count).IsEqualTo(2);
			await Assert.That(file.Records[0]).IsEqualTo(new DanmakuRecord.Connection(TimeSpan.Zero, true, false));
			await Assert.That(file.Records[1] is DanmakuRecord.Comment { Text: "没有通知" }).IsTrue();
		}
	}

	private static RoomDanmaku Create(TestDirectory directory, FakeTimeProvider time)
	{
		return new RoomDanmaku(1, time, NullLogger.Instance, static () => { });
	}

	private static DanmakuRun Run(TestDirectory directory, bool isEnabled = true)
	{
		DanmakuRunSettings settings = new(new DanmakuProfile(isEnabled, true, DanmakuGiftMode.None, true, TimeSpan.Zero, true, true, false, false), directory.Root, false, false);
		return new DanmakuRun(static () => new DanmakuFileHeader { RoomId = 1, Title = "title" }, _ => ValueTask.FromResult(settings));
	}

	private static DanmuMessage Connected(FakeTimeProvider time)
	{
		return new DanmuMessage.Connected(0) { ReceivedAt = new DanmuReceiveTime(time.GetTimestamp(), time.GetUtcNow()) };
	}

	private static DanmuMessage Comment(FakeTimeProvider time, string text)
	{
		return new DanmuMessage.Comment(new DanmuSender(0, "观众", GuardLevel.None, null), text, 1, 25, 0xFFFFFF, DateTimeOffset.UnixEpoch, "0", "0", DanmuCommentKind.Text, false) { ReceivedAt = new DanmuReceiveTime(time.GetTimestamp(), time.GetUtcNow()) };
	}

	// 队列里有没有工作和可读通知何时送达分开由测试控制：加入工作不送可读通知，只在完成时送达
	private sealed class ManualReader : ChannelReader<DanmakuWork>
	{
		private readonly Queue<DanmakuWork> _items = new();
		private readonly Lock _lock = new();
		private readonly TaskCompletionSource<bool> _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public override bool CanPeek => true;

		public void Add(DanmakuWork work)
		{
			lock (_lock)
			{
				_items.Enqueue(work);
			}
		}

		public void Complete()
		{
			_completed.TrySetResult(false);
		}

		public override bool TryRead([MaybeNullWhen(false)] out DanmakuWork item)
		{
			lock (_lock)
			{
				return _items.TryDequeue(out item);
			}
		}

		public override bool TryPeek([MaybeNullWhen(false)] out DanmakuWork item)
		{
			lock (_lock)
			{
				return _items.TryPeek(out item);
			}
		}

		public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
		{
			lock (_lock)
			{
				if (_items.Count > 0)
				{
					return ValueTask.FromResult(true);
				}
			}

			return _completed.Task.IsCompleted ? ValueTask.FromResult(TryPeek(out _)) : new ValueTask<bool>(_completed.Task.WaitAsync(cancellationToken));
		}
	}

	// 写入者的等待到期由计时器实现，建好计时器才说明它已经在等
	private sealed class TimerWatchingTimeProvider(FakeTimeProvider inner) : TimeProvider
	{
		public TaskCompletionSource TimerCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

		public override long TimestampFrequency => inner.TimestampFrequency;

		public override DateTimeOffset GetUtcNow()
		{
			return inner.GetUtcNow();
		}

		public override long GetTimestamp()
		{
			return inner.GetTimestamp();
		}

		public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
		{
			ITimer timer = inner.CreateTimer(callback, state, dueTime, period);
			TimerCreated.TrySetResult();
			return timer;
		}
	}

	// 每次放置取下一个由测试给出的结果；没有排好的结果时用 Fallback
	private sealed class ControlledTimeline : IRecordingTimeline
	{
		private readonly Queue<TaskCompletionSource<TimelinePosition?>> _results = new();
		private readonly Lock _lock = new();

		public TimelinePosition? Fallback { get; set; }

		public TaskCompletionSource Placing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public TaskCompletionSource<TimelinePosition?> Next()
		{
			TaskCompletionSource<TimelinePosition?> result = new(TaskCreationOptions.RunContinuationsAsynchronously);

			lock (_lock)
			{
				_results.Enqueue(result);
			}

			return result;
		}

		public async ValueTask<TimelinePosition?> PlaceAsync(long timestamp, CancellationToken cancellationToken)
		{
			TaskCompletionSource<TimelinePosition?>? result;
			Placing.TrySetResult();

			lock (_lock)
			{
				_results.TryDequeue(out result);
			}

			return result is null ? Fallback : await result.Task.WaitAsync(cancellationToken);
		}

		public bool TryPlace(long timestamp, out TimelinePosition? position)
		{
			throw new NotSupportedException();
		}

		public VideoResolution? GetResolution(string path)
		{
			return new VideoResolution(1280, 720);
		}

		public TimeSpan? GetDuration(string path)
		{
			return Duration(path);
		}

		public Func<string, TimeSpan?> Duration { get; set; } = static _ => TimeSpan.FromSeconds(30);
	}
}
