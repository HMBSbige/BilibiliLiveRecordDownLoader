using BilibiliLive.Danmu;
using BilibiliLiveRecordDownLoader.Application.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Recording;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Channels;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Danmaku;

// 写入者按队列顺序等每条弹幕的归属，“关闭段”标记之前的弹幕都写进那一段，之后才到的丢弃
public sealed class RoomDanmakuTest
{
	[Test]
	[Timeout(10_000)]
	public async Task SegmentIsClosedOnlyAfterTheDanmakuBeforeItArePlacedAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		ControlledTimeline timeline = new();
		await using RoomDanmaku danmaku = Create(time);
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

	// 段关闭之后它的弹幕文件可能已经随画面一起删掉
	[Test]
	[Timeout(10_000)]
	public async Task DanmakuArrivingAfterTheirSegmentClosedDoNotRecreateItsFileAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		ControlledTimeline timeline = new();
		await using RoomDanmaku danmaku = Create(time);
		string video = directory.Combine("a.flv");
		string xml = directory.Combine("a.xml");
		danmaku.SetRecording(true);
		danmaku.OnVideoStarted(timeline, video, Run(directory));
		await danmaku.CompleteSegment(video).WaitAsync(cancellationToken);
		File.Delete(xml);

		timeline.Next().SetResult(new TimelinePosition(0, video, TimeSpan.FromSeconds(4)));
		danmaku.OnReceived(Comment(time, "晚到"));
		danmaku.OnVideoEnded();
		await danmaku.DisposeAsync();

		await Assert.That(File.Exists(xml)).IsFalse();
	}

	[Test]
	[Timeout(10_000)]
	public async Task NextSegmentFileIsOpenedWhenItsDanmakuArriveBeforeTheStartNoticeAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		ControlledTimeline timeline = new();
		await using RoomDanmaku danmaku = Create(time);
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

	// 最后一段之后没有画面，丢弃
	[Test]
	[Timeout(10_000)]
	public async Task DanmakuPastTheEndOfASegmentContinueInTheNextOneAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		ControlledTimeline timeline = new() { Duration = static path => path.EndsWith("a.flv", StringComparison.Ordinal) ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(5) };
		await using RoomDanmaku danmaku = Create(time);
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

	// 上一段还开着时下一段的弹幕先压着
	[Test]
	[Timeout(10_000)]
	public async Task DanmakuHandedOverFromTheEndOfASegmentComeBeforeTheNextOnesOwnAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		ControlledTimeline timeline = new() { Duration = static path => path.EndsWith("a.flv", StringComparison.Ordinal) ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(5) };
		await using RoomDanmaku danmaku = Create(time);
		string first = directory.Combine("a.flv");
		string second = directory.Combine("b.flv");
		danmaku.SetRecording(true);
		danmaku.OnVideoStarted(timeline, first, Run(directory));

		timeline.Next().SetResult(new TimelinePosition(0, first, TimeSpan.FromSeconds(10.5)));
		danmaku.OnReceived(Comment(time, "推过末尾"));
		timeline.Next().SetResult(new TimelinePosition(1, second, TimeSpan.FromSeconds(1)));
		danmaku.OnReceived(Comment(time, "第二段"));
		await CloseSegmentsAsync(danmaku, first, second, cancellationToken);

		await Assert.That(await DescribeCommentsAsync(directory.Combine("b.xml"), cancellationToken)).IsEqualTo("推过末尾@0.5, 第二段@1");
	}

	// 段的边界按加上偏移、写进文件的时间分
	[Test]
	[Timeout(10_000)]
	public async Task PositiveOffsetMovesDanmakuPastTheEndIntoTheNextSegmentAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		ControlledTimeline timeline = new() { Duration = static path => path.EndsWith("a.flv", StringComparison.Ordinal) ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(5) };
		await using RoomDanmaku danmaku = Create(time);
		string first = directory.Combine("a.flv");
		string second = directory.Combine("b.flv");
		danmaku.SetRecording(true);
		danmaku.OnVideoStarted(timeline, first, Run(directory, static () => true, offset: TimeSpan.FromSeconds(2)));

		timeline.Next().SetResult(new TimelinePosition(0, first, TimeSpan.FromSeconds(7.5)));
		danmaku.OnReceived(Comment(time, "偏移后在末尾之前"));
		timeline.Next().SetResult(new TimelinePosition(0, first, TimeSpan.FromSeconds(8.5)));
		danmaku.OnReceived(Comment(time, "偏移后推过末尾"));
		Task<DanmakuSegment?> closed = danmaku.CompleteSegment(first);
		danmaku.OnSegmentStarted(second);
		await closed.WaitAsync(cancellationToken);
		timeline.Next().SetResult(new TimelinePosition(1, second, TimeSpan.FromSeconds(3.5)));
		danmaku.OnReceived(Comment(time, "偏移后在录制结束之后"));
		Task<DanmakuSegment?> last = danmaku.CompleteSegment(second);
		danmaku.OnVideoEnded();
		await last.WaitAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(await DescribeCommentsAsync(directory.Combine("a.xml"), cancellationToken)).IsEqualTo("偏移后在末尾之前@9.5");
			await Assert.That(await DescribeCommentsAsync(directory.Combine("b.xml"), cancellationToken)).IsEqualTo("偏移后推过末尾@0.5");
		}
	}

	// 落到段开头之前的写成 0
	[Test]
	[Timeout(10_000)]
	public async Task NegativeOffsetKeepsDanmakuPulledBackBeforeTheEndInTheSegmentAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		ControlledTimeline timeline = new() { Duration = static path => path.EndsWith("a.flv", StringComparison.Ordinal) ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(5) };
		await using RoomDanmaku danmaku = Create(time);
		string first = directory.Combine("a.flv");
		string second = directory.Combine("b.flv");
		danmaku.SetRecording(true);
		danmaku.OnVideoStarted(timeline, first, Run(directory, static () => true, offset: TimeSpan.FromSeconds(-2)));

		timeline.Next().SetResult(new TimelinePosition(0, first, TimeSpan.FromSeconds(11)));
		danmaku.OnReceived(Comment(time, "偏移后在末尾之前"));
		timeline.Next().SetResult(new TimelinePosition(0, first, TimeSpan.FromSeconds(12.5)));
		danmaku.OnReceived(Comment(time, "偏移后仍推过末尾"));
		Task<DanmakuSegment?> closed = danmaku.CompleteSegment(first);
		danmaku.OnSegmentStarted(second);
		await closed.WaitAsync(cancellationToken);
		timeline.Next().SetResult(new TimelinePosition(1, second, TimeSpan.FromSeconds(1)));
		danmaku.OnReceived(Comment(time, "偏移后在开头之前"));
		Task<DanmakuSegment?> last = danmaku.CompleteSegment(second);
		danmaku.OnVideoEnded();
		await last.WaitAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(await DescribeCommentsAsync(directory.Combine("a.xml"), cancellationToken)).IsEqualTo("偏移后在末尾之前@9");
			await Assert.That(await DescribeCommentsAsync(directory.Combine("b.xml"), cancellationToken)).IsEqualTo("偏移后仍推过末尾@0.5, 偏移后在开头之前@0");
		}
	}

	// 从没连上时断开的区间就是整段画面
	[Test]
	[Timeout(10_000)]
	[Arguments(2)]
	[Arguments(-2)]
	public async Task AGapCoveringTheWholeSegmentMatchesTheVideoWhateverTheOffsetAsync(int offsetSeconds, CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		ControlledTimeline timeline = new();
		await using RoomDanmaku danmaku = Create(time);
		string first = directory.Combine("a.flv");
		danmaku.SetRecording(true);
		danmaku.OnVideoStarted(timeline, first, Run(directory, static () => true, offset: TimeSpan.FromSeconds(offsetSeconds)));
		Task<DanmakuSegment?> closed = danmaku.CompleteSegment(first);
		danmaku.OnVideoEnded();
		await closed.WaitAsync(cancellationToken);

		DanmakuFile a = await DanmakuXmlReader.ReadFileAsync(directory.Combine("a.xml"), cancellationToken);

		await Assert.That(Describe(a.Records)).IsEqualTo(Describe([new DanmakuRecord.Gap(TimeSpan.Zero, TimeSpan.FromSeconds(30))]));
	}

	// 断开的起点是弹幕的位置，和弹幕一样加偏移；终点是这一段画面的结尾
	[Test]
	[Timeout(10_000)]
	[Arguments(2)]
	[Arguments(-2)]
	public async Task AGapLeftOpenEndsWithTheVideoWhateverTheOffsetAsync(int offsetSeconds, CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		ControlledTimeline timeline = new();
		await using RoomDanmaku danmaku = Create(time);
		string first = directory.Combine("a.flv");
		TimeSpan offset = TimeSpan.FromSeconds(offsetSeconds);
		danmaku.SetRecording(true);
		danmaku.OnReceived(Connected(time));
		danmaku.OnReceived(LoginNotice(time));
		danmaku.OnVideoStarted(timeline, first, Run(directory, static () => true, offset: offset));
		timeline.Next().SetResult(new TimelinePosition(0, first, TimeSpan.FromSeconds(10)));
		danmaku.OnDisconnected();
		Task<DanmakuSegment?> closed = danmaku.CompleteSegment(first);
		danmaku.OnVideoEnded();
		await closed.WaitAsync(cancellationToken);

		DanmakuFile a = await DanmakuXmlReader.ReadFileAsync(directory.Combine("a.xml"), cancellationToken);

		await Assert.That(Describe(a.Records.OfType<DanmakuRecord.Gap>())).IsEqualTo(Describe([new DanmakuRecord.Gap(TimeSpan.FromSeconds(10) + offset, TimeSpan.FromSeconds(30))]));
	}

	// 文件开头的连接状态是画面开头的状态
	[Test]
	[Timeout(10_000)]
	[Arguments(3, true)]
	[Arguments(-3, true)]
	[Arguments(3, false)]
	[Arguments(-3, false)]
	public async Task TheConnectionAtTheStartOfAFileIsAtZeroWhateverTheOffsetAsync(int offsetSeconds, bool isSettledBeforeTheFile, CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		ControlledTimeline timeline = new();
		await using RoomDanmaku danmaku = Create(time);
		string first = directory.Combine("a.flv");
		danmaku.SetRecording(true);
		danmaku.OnReceived(Connected(time));

		if (isSettledBeforeTheFile)
		{
			danmaku.OnReceived(LoginNotice(time));
		}

		danmaku.OnVideoStarted(timeline, first, Run(directory, static () => true, offset: TimeSpan.FromSeconds(offsetSeconds)));

		if (!isSettledBeforeTheFile)
		{
			danmaku.OnReceived(LoginNotice(time));
		}

		Task<DanmakuSegment?> closed = danmaku.CompleteSegment(first);
		danmaku.OnVideoEnded();
		await closed.WaitAsync(cancellationToken);

		DanmakuFile a = await DanmakuXmlReader.ReadFileAsync(directory.Combine("a.xml"), cancellationToken);

		await Assert.That(Describe(a.Records.OfType<DanmakuRecord.Connection>())).IsEqualTo(Describe([new DanmakuRecord.Connection(TimeSpan.Zero, true, true)]));
	}

	// 录制结束时还没等到通知的文件，开头按未打码记下连接
	[Test]
	[Timeout(10_000)]
	[Arguments(3)]
	[Arguments(-3)]
	public async Task AConnectionStillAwaitedWhenRecordingEndsIsAtZeroWhateverTheOffsetAsync(int offsetSeconds, CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		ControlledTimeline timeline = new();
		RoomDanmaku danmaku = Create(time);
		danmaku.SetRecording(true);
		danmaku.OnReceived(Connected(time));
		danmaku.OnVideoStarted(timeline, directory.Combine("a.flv"), Run(directory, static () => true, offset: TimeSpan.FromSeconds(offsetSeconds)));
		await danmaku.DisposeAsync();

		DanmakuFile a = await DanmakuXmlReader.ReadFileAsync(directory.Combine("a.xml"), cancellationToken);

		await Assert.That(Describe(a.Records.OfType<DanmakuRecord.Connection>())).IsEqualTo(Describe([new DanmakuRecord.Connection(TimeSpan.Zero, true, false)]));
	}

	// 改写偏移靠它避开正在写的文件
	[Test]
	[Timeout(10_000)]
	public async Task OpenFileIsReportedAsWritingUntilTheSegmentClosesAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		await using RoomDanmaku danmaku = Create(time);
		string video = directory.Combine("a.flv");
		string xml = directory.Combine("a.xml");
		danmaku.SetRecording(true);
		danmaku.OnVideoStarted(new ControlledTimeline(), video, Run(directory));

		await TestWait.UntilAsync(() => danmaku.IsWriting(xml), "the danmaku file is opened", cancellationToken);

		DanmakuSegment? segment = await danmaku.CompleteSegment(video).WaitAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(segment?.Path).IsEqualTo(xml);
			await Assert.That(danmaku.IsWriting(xml)).IsFalse();
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task SegmentWithoutDanmakuRecordingHasNoFileAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		await using RoomDanmaku danmaku = Create(time);
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
		await using RoomDanmaku danmaku = Create(time);
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

	// 开播时房间开始记录常常落在认证和未登录通知之间
	[Test]
	[Timeout(10_000)]
	public async Task AFileStartedBeforeTheLoginNoticeGetsOneMaskedConnectionAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		await using RoomDanmaku danmaku = Create(time);
		danmaku.SetRecording(true);
		danmaku.OnReceived(Connected(time));
		danmaku.StartTextOnly(Run(directory));
		danmaku.OnReceived(LoginNotice(time));
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

	// 队列的可读通知是异步送达的，通知排进队列时等待可能已经先到期
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
		reader.Add(new DanmakuWork.Received(LoginNotice(time)));
		time.Advance(DanmakuRecorder.LoginNoticeWait * 2);
		reader.Add(new DanmakuWork.TextOnlyStopped());
		reader.Complete();
		await running.WaitAsync(cancellationToken);

		DanmakuFile file = await DanmakuXmlReader.ReadFileAsync(Directory.GetFiles(directory.Root, "*.xml", SearchOption.AllDirectories).Single(), cancellationToken);

		await Assert.That(file.Records.OfType<DanmakuRecord.Connection>().ToArray()).IsEquivalentTo([new DanmakuRecord.Connection(TimeSpan.Zero, true, true)], EqualityComparer<DanmakuRecord.Connection>.Default);
	}

	// 没等到通知就有弹幕时按未打码记下连接
	[Test]
	[Timeout(10_000)]
	public async Task AFileStartedBeforeTheConnectionIsSettledStillStartsConnectedAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		await using RoomDanmaku danmaku = Create(time);
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

	// 关闭时再写一次，仍然失败也只记一条警告
	[Test]
	[Timeout(10_000)]
	public async Task AFileThatCannotBeWrittenIsGivenUpWithoutRetryingAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		TimerWatchingTimeProvider watched = new(time);
		ListLogger logger = new();
		HookedStream stream = new(static index => index > 1 ? throw new IOException("磁盘已满") : Task.CompletedTask);
		bool isRecording = false;
		DanmakuRecorder recorder = new(1, watched, logger, new DanmuReceiveOptions(), recording => isRecording = recording)
		{
			CreateWriterAsync = (_, header, token) => DanmakuXmlWriter.CreateAsync(stream, header, DanmakuFlushPolicy.Default, time, token)
		};
		Channel<DanmakuWork> queue = Channel.CreateUnbounded<DanmakuWork>();
		queue.Writer.TryWrite(new DanmakuWork.TextOnlyStarted(Run(directory), time.GetTimestamp()));
		queue.Writer.TryWrite(new DanmakuWork.Received(Comment(time, "写不进去")));
		Task running = Task.Run(() => recorder.RunAsync(queue.Reader, static _ => { }, cancellationToken), cancellationToken);

		// 写入者开始等按时刷新，到期后写失败
		await watched.TimerCreated.Task.WaitAsync(cancellationToken);
		time.Advance(DanmakuFlushPolicy.Default.Interval);
		await TestWait.UntilAsync(() => stream.Writes >= 2, "the due flush is attempted", cancellationToken);
		await Task.Delay(200, cancellationToken);
		int writes = stream.Writes;
		bool recordingAfterFailure = isRecording;

		queue.Writer.TryWrite(new DanmakuWork.TextOnlyStopped());
		queue.Writer.Complete();
		await running.WaitAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(writes).IsEqualTo(2);
			await Assert.That(recordingAfterFailure).IsFalse();
			await Assert.That(stream.Writes).IsEqualTo(3);
			await Assert.That(logger.Entries.Count(static entry => entry.Level is LogLevel.Warning)).IsEqualTo(1);
		}
	}

	// 关闭时从原位置重写，盖掉半条记录
	[Test]
	[Timeout(10_000)]
	public async Task AFileThatFailedHalfwayIsCompletedWhenClosedAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		TimerWatchingTimeProvider watched = new(time);
		ListLogger logger = new();
		HalfWrittenOnceStream stream = new(failAt: 2);
		DanmakuRecorder recorder = new(1, watched, logger, new DanmuReceiveOptions(), static _ => { })
		{
			CreateWriterAsync = (_, header, token) => DanmakuXmlWriter.CreateAsync(stream, header, DanmakuFlushPolicy.Default, time, token)
		};
		Channel<DanmakuWork> queue = Channel.CreateUnbounded<DanmakuWork>();
		queue.Writer.TryWrite(new DanmakuWork.TextOnlyStarted(Run(directory), time.GetTimestamp()));
		queue.Writer.TryWrite(new DanmakuWork.Received(Comment(time, "写到一半")));
		Task running = Task.Run(() => recorder.RunAsync(queue.Reader, static _ => { }, cancellationToken), cancellationToken);

		await watched.TimerCreated.Task.WaitAsync(cancellationToken);
		time.Advance(DanmakuFlushPolicy.Default.Interval);
		await TestWait.UntilAsync(() => stream.Writes >= 2, "the due flush is attempted", cancellationToken);
		queue.Writer.TryWrite(new DanmakuWork.TextOnlyStopped());
		queue.Writer.Complete();
		await running.WaitAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(DanmakuSamples.ParseStrictly(stream.ToArray()).GetValueOrDefault(@"d")).IsEqualTo(1);
			await Assert.That(logger.Entries.Count(static entry => entry.Level is LogLevel.Warning)).IsEqualTo(1);
		}
	}

	// 建文件到关上之间都不能改写偏移
	[Test]
	[Timeout(10_000)]
	public async Task AFileCountsAsBeingWrittenFromCreationUntilItIsClosedAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		TaskCompletionSource headerBlocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource headerGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource closeBlocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource closeGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
		HookedStream stream = new(index => index switch
		{
			1 => BlockAsync(headerBlocked, headerGate),
			2 => BlockAsync(closeBlocked, closeGate),
			_ => Task.CompletedTask
		});
		string? path = null;
		DanmakuRecorder recorder = new(1, time, NullLogger.Instance, new DanmuReceiveOptions(), static _ => { })
		{
			CreateWriterAsync = (created, header, token) =>
			{
				path = created;
				return DanmakuXmlWriter.CreateAsync(stream, header, DanmakuFlushPolicy.Default, time, token);
			}
		};
		Channel<DanmakuWork> queue = Channel.CreateUnbounded<DanmakuWork>();
		queue.Writer.TryWrite(new DanmakuWork.TextOnlyStarted(Run(directory), time.GetTimestamp()));
		Task running = Task.Run(() => recorder.RunAsync(queue.Reader, static _ => { }, cancellationToken), cancellationToken);

		await headerBlocked.Task.WaitAsync(cancellationToken);
		bool whileCreating = path is not null && recorder.IsWriting(path);
		headerGate.SetResult();

		queue.Writer.TryWrite(new DanmakuWork.Received(Comment(time, "最后一条")));
		queue.Writer.TryWrite(new DanmakuWork.TextOnlyStopped());
		await closeBlocked.Task.WaitAsync(cancellationToken);
		bool whileClosing = path is not null && recorder.IsWriting(path);
		closeGate.SetResult();

		queue.Writer.Complete();
		await running.WaitAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(whileCreating).IsTrue();
			await Assert.That(whileClosing).IsTrue();
			await Assert.That(path is not null && recorder.IsWriting(path)).IsFalse();
		}

		static async Task BlockAsync(TaskCompletionSource blocked, TaskCompletionSource gate)
		{
			blocked.TrySetResult();
			await gate.Task;
		}
	}

	// 弹幕连接每收完一帧才读一次解析开关，最后一个查看的人离开时这一帧剩下的消息还会交过来
	[Test]
	public async Task MessagesLeftInAFrameAfterTheLastViewerLeavesDoNotReachTheFeedAsync()
	{
		FakeTimeProvider time = new();
		await using RoomDanmaku danmaku = Create(time);
		IDisposable subscription = danmaku.Subscribe();
		danmaku.OnReceived(Comment(time, "看着呢"));
		subscription.Dispose();
		danmaku.OnReceived(Comment(time, "这一帧剩下的"));
		using IDisposable again = danmaku.Subscribe();

		await Assert.That(danmaku.Feed.GetEntries(0)).IsEmpty();
	}

	// 重连落在下一段、段开始的通知还没到时，当场建出的下一段文件开头是断开的；前一段断开到它的末尾
	[Test]
	[Timeout(10_000)]
	public async Task AReconnectionThatOpensTheNextSegmentStartsItDisconnectedAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		ControlledTimeline timeline = new();
		await using RoomDanmaku danmaku = Create(time);
		string first = directory.Combine("a.flv");
		string second = directory.Combine("b.flv");
		danmaku.SetRecording(true);
		danmaku.OnReceived(Connected(time));
		danmaku.OnReceived(LoginNotice(time));
		danmaku.OnVideoStarted(timeline, first, Run(directory));
		timeline.Next().SetResult(new TimelinePosition(0, first, TimeSpan.FromSeconds(10)));
		danmaku.OnDisconnected();
		danmaku.OnReceived(Connected(time));
		timeline.Next().SetResult(new TimelinePosition(1, second, TimeSpan.FromSeconds(5)));
		danmaku.OnReceived(LoginNotice(time));
		await CloseSegmentsAsync(danmaku, first, second, cancellationToken);

		DanmakuFile a = await DanmakuXmlReader.ReadFileAsync(directory.Combine("a.xml"), cancellationToken);
		DanmakuFile b = await DanmakuXmlReader.ReadFileAsync(directory.Combine("b.xml"), cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(Describe(a.Records)).IsEqualTo(Describe([new DanmakuRecord.Connection(TimeSpan.Zero, true, true), new DanmakuRecord.Gap(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30))]));
			await Assert.That(Describe(b.Records)).IsEqualTo(Describe([new DanmakuRecord.Gap(TimeSpan.Zero, TimeSpan.FromSeconds(5)), new DanmakuRecord.Connection(TimeSpan.FromSeconds(5), true, true)]));
		}
	}

	// 断开落在下一段、段开始的通知还没到时，当场建出的下一段文件开头是连着的，从断开的位置起算断开
	[Test]
	[Timeout(10_000)]
	public async Task ADisconnectionThatOpensTheNextSegmentStartsItConnectedAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		ControlledTimeline timeline = new();
		await using RoomDanmaku danmaku = Create(time);
		string first = directory.Combine("a.flv");
		string second = directory.Combine("b.flv");
		danmaku.SetRecording(true);
		danmaku.OnReceived(Connected(time));
		danmaku.OnReceived(LoginNotice(time));
		danmaku.OnVideoStarted(timeline, first, Run(directory));
		timeline.Next().SetResult(new TimelinePosition(1, second, TimeSpan.FromSeconds(3)));
		danmaku.OnDisconnected();
		await CloseSegmentsAsync(danmaku, first, second, cancellationToken);

		DanmakuFile b = await DanmakuXmlReader.ReadFileAsync(directory.Combine("b.xml"), cancellationToken);

		await Assert.That(Describe(b.Records)).IsEqualTo(Describe([new DanmakuRecord.Connection(TimeSpan.Zero, true, true), new DanmakuRecord.Gap(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(30))]));
	}

	[Test]
	[Timeout(10_000)]
	public async Task ALoginNoticeAfterTheConnectionIsRecordedAddsAMaskedConnectionAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		await using RoomDanmaku danmaku = Create(time);
		danmaku.SetRecording(true);
		danmaku.StartTextOnly(Run(directory));
		danmaku.OnReceived(Connected(time));
		danmaku.OnReceived(Comment(time, "通知之前"));
		time.Advance(TimeSpan.FromSeconds(1));
		danmaku.OnReceived(LoginNotice(time));
		danmaku.StopTextOnly();
		await danmaku.DisposeAsync();

		DanmakuFile file = await DanmakuXmlReader.ReadFileAsync(Directory.GetFiles(directory.Root, "*.xml", SearchOption.AllDirectories).Single(), cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(file.Records.Count).IsEqualTo(3);
			await Assert.That(Describe(file.Records.OfType<DanmakuRecord.Connection>())).IsEqualTo(Describe([new DanmakuRecord.Connection(TimeSpan.Zero, true, false), new DanmakuRecord.Connection(TimeSpan.FromSeconds(1), true, true)]));
		}
	}

	// 后处理拿到的是整段的时长
	[Test]
	[Timeout(10_000)]
	public async Task TurningDanmakuOffAndOnWithinASegmentKeepsWritingTheSameFileAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		ListLogger logger = new();
		bool isEnabled = true;
		TimeSpan duration = TimeSpan.FromSeconds(10);
		string video = directory.Combine("a.flv");
		ControlledTimeline timeline = new() { Fallback = new TimelinePosition(0, video, TimeSpan.FromSeconds(1)), Duration = _ => duration };
		await using RoomDanmaku danmaku = new(1, time, logger, static () => { });
		danmaku.SetRecording(true);
		danmaku.OnVideoStarted(timeline, video, Run(directory, () => isEnabled));
		danmaku.OnReceived(Comment(time, "关之前"));
		await BarrierAsync(danmaku, directory, cancellationToken);

		isEnabled = false;
		danmaku.OnSettingsChanged();
		danmaku.OnReceived(Comment(time, "关着"));
		await BarrierAsync(danmaku, directory, cancellationToken);
		bool recordingWhileOff = danmaku.Snapshot.IsRecording;

		isEnabled = true;
		danmaku.OnSettingsChanged();
		danmaku.OnReceived(Comment(time, "又打开"));
		await BarrierAsync(danmaku, directory, cancellationToken);
		duration = TimeSpan.FromSeconds(20);
		DanmakuSegment? segment = await danmaku.CompleteSegment(video).WaitAsync(cancellationToken);
		danmaku.OnVideoEnded();
		await danmaku.DisposeAsync();

		DanmakuFile file = await DanmakuXmlReader.ReadFileAsync(directory.Combine("a.xml"), cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(string.Join(", ", file.Records.OfType<DanmakuRecord.Comment>().Select(static comment => comment.Text))).IsEqualTo("关之前, 又打开");
			await Assert.That(segment?.Duration).IsEqualTo(TimeSpan.FromSeconds(20));
			await Assert.That(recordingWhileOff).IsFalse();
			await Assert.That(logger.Entries.Any(static entry => entry.Message.Contains("after their file was closed", StringComparison.Ordinal))).IsFalse();
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task RawMessagesAreNotParsedForAVideoWithoutDanmakuAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		await using RoomDanmaku danmaku = Create(time);
		danmaku.OnVideoStarted(new ControlledTimeline(), directory.Combine("a.flv"), Run(directory, static () => false, saveRaw: true));
		await BarrierAsync(danmaku, directory, cancellationToken);

		await Assert.That(danmaku.Options.IncludeRaw).IsFalse();
	}

	// 又打开时开头补记当前的连接
	[Test]
	[Timeout(10_000)]
	public async Task RawMessagesStopWhileDanmakuIsOffAndContinueInTheSameFileAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		bool isEnabled = true;
		string video = directory.Combine("a.flv");
		ControlledTimeline timeline = new() { Fallback = new TimelinePosition(0, video, TimeSpan.FromSeconds(1)) };
		await using RoomDanmaku danmaku = Create(time);
		string first = UnixMilliseconds(time);
		danmaku.SetRecording(true);
		danmaku.OnVideoStarted(timeline, video, Run(directory, () => isEnabled, saveRaw: true));
		danmaku.OnReceived(Connected(time));
		danmaku.OnReceived(LoginNotice(time));
		await BarrierAsync(danmaku, directory, cancellationToken);
		bool rawWhileOn = danmaku.Options.IncludeRaw;

		isEnabled = false;
		danmaku.OnSettingsChanged();
		await BarrierAsync(danmaku, directory, cancellationToken);
		bool rawWhileOff = danmaku.Options.IncludeRaw;
		string raw = Directory.GetFiles(directory.Root, "*.jsonl").Single();
		long lengthWhileOff = new FileInfo(raw).Length;
		time.Advance(TimeSpan.FromSeconds(1));
		string second = UnixMilliseconds(time);
		danmaku.OnDisconnected();
		danmaku.OnReceived(Connected(time));
		danmaku.OnReceived(LoginNotice(time));
		await BarrierAsync(danmaku, directory, cancellationToken);
		long lengthAfterReconnecting = new FileInfo(raw).Length;

		isEnabled = true;
		danmaku.OnSettingsChanged();
		danmaku.OnReceived(Raw(time, "DANMU_MSG"));
		await danmaku.CompleteSegment(video).WaitAsync(cancellationToken);
		danmaku.OnVideoEnded();
		danmaku.EndSession();
		await danmaku.DisposeAsync();

		string[] lines = await File.ReadAllLinesAsync(raw, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(rawWhileOn).IsTrue();
			await Assert.That(rawWhileOff).IsFalse();
			await Assert.That(lengthAfterReconnecting).IsEqualTo(lengthWhileOff);
			await Assert.That(Directory.GetFiles(directory.Root, "*.jsonl").Length).IsEqualTo(1);
			await Assert.That(string.Join('\n', lines)).IsEqualTo(string.Join('\n', "{\"t\":" + first + ",\"cmd\":\"@connected\",\"raw\":{\"anonymous\":true}}", "{\"t\":" + second + ",\"cmd\":\"@connected\",\"raw\":{\"anonymous\":true,\"masked\":true}}", "{\"t\":" + second + ",\"cmd\":\"DANMU_MSG\",\"raw\":{\"cmd\":\"DANMU_MSG\"}}"));
		}
	}

	// 补记的时刻是连接建立的时刻
	[Test]
	[Timeout(10_000)]
	public async Task RawMessagesStartedBeforeTheConnectionIsSettledRecordItOnceSettledAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		await using RoomDanmaku danmaku = Create(time);
		string connectedAt = UnixMilliseconds(time);
		danmaku.SetRecording(true);
		danmaku.OnReceived(Connected(time));
		time.Advance(TimeSpan.FromSeconds(1));
		danmaku.StartTextOnly(Run(directory, static () => true, saveRaw: true));
		danmaku.OnReceived(LoginNotice(time));
		danmaku.StopTextOnly();
		danmaku.EndSession();
		await danmaku.DisposeAsync();

		string[] lines = await File.ReadAllLinesAsync(Directory.GetFiles(directory.Root, "*.jsonl", SearchOption.AllDirectories).Single(), cancellationToken);

		await Assert.That(string.Join('\n', lines)).IsEqualTo("{\"t\":" + connectedAt + ",\"cmd\":\"@connected\",\"raw\":{\"anonymous\":true,\"masked\":true}}");
	}

	// 写不进去时停到下一个文件
	[Test]
	[Timeout(10_000)]
	public async Task RawMessagesContinueInTheSameFileAfterAWriteFailureAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		FakeTimeProvider time = new();
		DanmuReceiveOptions options = new();
		int opened = 0;
		bool rawAfterFailure = true;
		DanmakuRecorder recorder = new(1, time, NullLogger.Instance, options, static _ => { })
		{
			OpenRawWriter = path => ++opened is 1 ? RawDanmakuWriter.Create(new HookedStream(static _ => throw new IOException("磁盘已满")), new DanmakuFlushPolicy(TimeSpan.FromSeconds(5), 1), time) : RawDanmakuWriter.Open(path, DanmakuFlushPolicy.Default, time)
		};
		Channel<DanmakuWork> queue = Channel.CreateUnbounded<DanmakuWork>();
		queue.Writer.TryWrite(new DanmakuWork.TextOnlyStarted(Run(directory, static () => true, saveRaw: true, splitOnTitleChange: true), time.GetTimestamp()));
		queue.Writer.TryWrite(new DanmakuWork.Received(Raw(time, "FAILED")));
		queue.Writer.TryWrite(new DanmakuWork.Received(Raw(time, "DROPPED")));
		queue.Writer.TryWrite(new DanmakuWork.TitleChanged("new title", time.GetTimestamp()));
		queue.Writer.TryWrite(new DanmakuWork.Received(Raw(time, "WRITTEN")));
		queue.Writer.TryWrite(new DanmakuWork.TextOnlyStopped());
		queue.Writer.TryWrite(new DanmakuWork.SessionEnded());
		queue.Writer.Complete();

		await recorder.RunAsync(queue.Reader, work =>
		{
			if (work is DanmakuWork.TitleChanged)
			{
				rawAfterFailure = options.IncludeRaw;
			}
		}, cancellationToken);

		string firstFile = await FindFileTitledAsync(directory, "title", cancellationToken);
		string[] raws = Directory.GetFiles(directory.Root, "*.jsonl", SearchOption.AllDirectories);
		string[] lines = await File.ReadAllLinesAsync(raws.Single(), cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(opened).IsEqualTo(2);
			await Assert.That(rawAfterFailure).IsFalse();
			await Assert.That(raws.Single()).IsEqualTo(firstFile[..^RecordingFileLayout.DanmakuExtension.Length] + RecordingFileLayout.RawDanmakuExtension);
			await Assert.That(string.Join('\n', lines.Select(static line => JsonNode.Parse(line)?["cmd"]?.GetValue<string>()))).IsEqualTo("WRITTEN");
		}
	}

	private static RoomDanmaku Create(FakeTimeProvider time)
	{
		return new RoomDanmaku(1, time, NullLogger.Instance, static () => { });
	}

	private static DanmakuRun Run(TestDirectory directory, bool isEnabled = true)
	{
		return Run(directory, () => isEnabled);
	}

	// 每次开文件或设置变化时读一次 isEnabled
	private static DanmakuRun Run(TestDirectory directory, Func<bool> isEnabled, bool saveRaw = false, bool splitOnTitleChange = false, TimeSpan offset = default)
	{
		return new DanmakuRun(static () => new DanmakuFileHeader { RoomId = 1, Title = "title" }, _ => ValueTask.FromResult(new DanmakuRunSettings(new DanmakuProfile(isEnabled(), true, DanmakuGiftMode.None, true, offset, true, true, saveRaw, false), directory.Root, false, splitOnTitleChange)));
	}

	// 写入者按顺序处理，这个关闭请求有了结果就说明之前的工作都已处理完
	private static async Task BarrierAsync(RoomDanmaku danmaku, TestDirectory directory, CancellationToken cancellationToken)
	{
		await danmaku.CompleteSegment(directory.Combine("barrier.flv")).WaitAsync(cancellationToken);
	}

	private static async Task CloseSegmentsAsync(RoomDanmaku danmaku, string first, string second, CancellationToken cancellationToken)
	{
		Task<DanmakuSegment?> closed = danmaku.CompleteSegment(first);
		danmaku.OnSegmentStarted(second);
		await closed.WaitAsync(cancellationToken);
		Task<DanmakuSegment?> last = danmaku.CompleteSegment(second);
		danmaku.OnVideoEnded();
		await last.WaitAsync(cancellationToken);
	}

	private static async Task<string> FindFileTitledAsync(TestDirectory directory, string title, CancellationToken cancellationToken)
	{
		foreach (string path in Directory.GetFiles(directory.Root, "*" + RecordingFileLayout.DanmakuExtension, SearchOption.AllDirectories))
		{
			if ((await DanmakuXmlReader.ReadFileAsync(path, cancellationToken)).Source is DanmakuFileSource.Native { Header.Title: var found } && found == title)
			{
				return path;
			}
		}

		throw new FileNotFoundException(title);
	}

	private static async Task<string> DescribeCommentsAsync(string path, CancellationToken cancellationToken)
	{
		DanmakuFile file = await DanmakuXmlReader.ReadFileAsync(path, cancellationToken);
		return string.Join(", ", file.Records.OfType<DanmakuRecord.Comment>().Select(static comment => $"{comment.Text}@{comment.Time.TotalSeconds}"));
	}

	private static string Describe(IEnumerable<DanmakuRecord> records)
	{
		return string.Join(", ", records);
	}

	private static string UnixMilliseconds(FakeTimeProvider time)
	{
		return time.GetUtcNow().ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
	}

	private static DanmuMessage Connected(FakeTimeProvider time)
	{
		return new DanmuMessage.Connected(0) { ReceivedAt = new DanmuReceiveTime(time.GetTimestamp(), time.GetUtcNow()) };
	}

	private static DanmuMessage LoginNotice(FakeTimeProvider time)
	{
		return new DanmuMessage.LoginNotice("未登录") { ReceivedAt = new DanmuReceiveTime(time.GetTimestamp(), time.GetUtcNow()) };
	}

	private static DanmuMessage Raw(FakeTimeProvider time, string command)
	{
		return new DanmuMessage.Raw(command, Encoding.UTF8.GetBytes($$"""{"cmd":"{{command}}"}""")) { ReceivedAt = new DanmuReceiveTime(time.GetTimestamp(), time.GetUtcNow()) };
	}

	private static DanmuMessage Comment(FakeTimeProvider time, string text)
	{
		return new DanmuMessage.Comment(new DanmuSender(0, "观众", GuardLevel.None, null), text, 1, 25, 0xFFFFFF, DateTimeOffset.UnixEpoch, "0", DanmuCommentKind.Text, false) { ReceivedAt = new DanmuReceiveTime(time.GetTimestamp(), time.GetUtcNow()) };
	}

	// 加入工作不送可读通知，只在完成时送达
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

	// beforeWrite 的参数是第几次写入，从 1 开始
	private sealed class HookedStream(Func<int, Task> beforeWrite) : MemoryStream
	{
		private int _writes;

		public int Writes => Volatile.Read(ref _writes);

		public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
		{
			await beforeWrite(Interlocked.Increment(ref _writes));
			await base.WriteAsync(buffer, cancellationToken);
		}
	}

	private sealed class HalfWrittenOnceStream(int failAt) : MemoryStream
	{
		private int _writes;

		public int Writes => Volatile.Read(ref _writes);

		public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
		{
			if (Interlocked.Increment(ref _writes) == failAt)
			{
				await base.WriteAsync(buffer.Slice(0, buffer.Length / 2), cancellationToken);
				throw new IOException("磁盘已满");
			}

			await base.WriteAsync(buffer, cancellationToken);
		}
	}

	private sealed class ListLogger : ILogger
	{
		private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();

		public IReadOnlyCollection<(LogLevel Level, string Message)> Entries => _entries;

		public IDisposable? BeginScope<TState>(TState state) where TState : notnull
		{
			return null;
		}

		public bool IsEnabled(LogLevel logLevel)
		{
			return true;
		}

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
		{
			_entries.Enqueue((logLevel, formatter(state, exception)));
		}
	}
}
