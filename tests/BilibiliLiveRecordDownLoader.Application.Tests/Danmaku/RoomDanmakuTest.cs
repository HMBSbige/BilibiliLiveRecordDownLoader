using BilibiliLive.Danmu;
using BilibiliLiveRecordDownLoader.Application.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Threading.Channels;

// 先释放以等文件写完再读；await using 兜底中途失败的情况
// ReSharper disable DisposeOnUsingVariable

namespace BilibiliLiveRecordDownLoader.Application.Tests.Danmaku;

public sealed class RoomDanmakuTest : IDisposable
{
	private readonly TestDirectory _directory = new();
	private readonly TimerSignalingTimeProvider _time = new();

	public void Dispose()
	{
		_directory.Dispose();
	}

	[Test]
	public async Task SegmentIsClosedOnlyAfterTheDanmakuBeforeItArePlacedAsync(CancellationToken cancellationToken)
	{
		ControlledTimeline timeline = new();
		await using RoomDanmaku danmaku = Create();
		string video = _directory.Combine("a.flv");
		danmaku.SetRecording(true);
		danmaku.OnReceived(Connected());
		danmaku.OnVideoStarted(timeline, video, Run());

		TaskCompletionSource<TimelinePosition?> pending = timeline.Next();
		danmaku.OnReceived(Comment("还在等"));
		Task<DanmakuSegment?> closed = danmaku.CompleteSegment(video);

		// 写入者卡在这一条的归属上
		await timeline.Placing.Task.WaitAsync(cancellationToken);
		bool closedEarly = closed.IsCompleted;
		pending.SetResult(new TimelinePosition(0, video, TimeSpan.FromSeconds(3)));
		DanmakuSegment? segment = await closed.WaitAsync(cancellationToken);

		// 关闭之后才到、按时间却属于这一段的弹幕不再写入
		timeline.Next().SetResult(new TimelinePosition(0, video, TimeSpan.FromSeconds(4)));
		danmaku.OnReceived(Comment("晚到"));
		danmaku.OnVideoEnded();
		await danmaku.DisposeAsync();
		(string Text, double Seconds)[] comments = await CommentsAsync("a.xml", cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(closedEarly).IsFalse();
			await Assert.That(segment).IsEqualTo(new DanmakuSegment(_directory.Combine("a.xml"), new VideoResolution(1280, 720), TimeSpan.FromSeconds(30)));
			await Assert.That(comments).IsEquivalentTo([("还在等", 3.0)], CollectionOrdering.Matching);
		}
	}

	// 段关闭之后它的弹幕文件可能已经随画面一起删掉
	[Test]
	public async Task DanmakuArrivingAfterTheirSegmentClosedDoNotRecreateItsFileAsync(CancellationToken cancellationToken)
	{
		ControlledTimeline timeline = new();
		await using RoomDanmaku danmaku = Create();
		string video = _directory.Combine("a.flv");
		string xml = _directory.Combine("a.xml");
		danmaku.SetRecording(true);
		danmaku.OnVideoStarted(timeline, video, Run());
		await danmaku.CompleteSegment(video).WaitAsync(cancellationToken);
		File.Delete(xml);

		timeline.Next().SetResult(new TimelinePosition(0, video, TimeSpan.FromSeconds(4)));
		danmaku.OnReceived(Comment("晚到"));
		danmaku.OnVideoEnded();
		await danmaku.DisposeAsync();

		await Assert.That(File.Exists(xml)).IsFalse();
	}

	[Test]
	public async Task NextSegmentFileIsOpenedWhenItsDanmakuArriveBeforeTheStartNoticeAsync(CancellationToken cancellationToken)
	{
		ControlledTimeline timeline = new();
		await using RoomDanmaku danmaku = Create();
		string first = _directory.Combine("a.flv");
		string second = _directory.Combine("b.flv");
		danmaku.SetRecording(true);
		danmaku.OnVideoStarted(timeline, first, Run());

		timeline.Next().SetResult(new TimelinePosition(1, second, TimeSpan.FromSeconds(1)));
		danmaku.OnReceived(Comment("第二段"));
		await CloseSegmentsAsync(danmaku, first, second, cancellationToken);

		await Assert.That(await CommentsAsync("b.xml", cancellationToken)).IsEquivalentTo([("第二段", 1.0)], CollectionOrdering.Matching);
	}

	// 一、二放在第一段，三放在第二段，Positions 是放置的位置；第二段是最后一段，之后没有画面，推过它末尾的丢弃
	public static IEnumerable<Func<(int OffsetSeconds, double[] Positions, (string Text, double Seconds)[] InFirst, (string Text, double Seconds)[] InSecond)>> SegmentEndCases()
	{
		yield return static () => (0, [9.5, 10.5, 6], [("一", 9.5)], [("二", 0.5)]);
		// 段的边界按加上偏移、写进文件的时间分
		yield return static () => (2, [7.5, 8.5, 3.5], [("一", 9.5)], [("二", 0.5)]);
		// 落到段开头之前的写成 0
		yield return static () => (-2, [11, 12.5, 1], [("一", 9)], [("二", 0.5), ("三", 0)]);
	}

	[Test]
	[MethodDataSource(nameof(SegmentEndCases))]
	public async Task DanmakuPastTheEndOfASegmentContinueInTheNextOneAsync(int offsetSeconds, double[] positions, (string Text, double Seconds)[] inFirst, (string Text, double Seconds)[] inSecond, CancellationToken cancellationToken)
	{
		ControlledTimeline timeline = new() { Duration = SegmentDuration };
		await using RoomDanmaku danmaku = Create();
		string first = _directory.Combine("a.flv");
		string second = _directory.Combine("b.flv");
		danmaku.SetRecording(true);
		danmaku.OnVideoStarted(timeline, first, Run(offset: TimeSpan.FromSeconds(offsetSeconds)));

		timeline.Next().SetResult(new TimelinePosition(0, first, TimeSpan.FromSeconds(positions[0])));
		danmaku.OnReceived(Comment("一"));
		timeline.Next().SetResult(new TimelinePosition(0, first, TimeSpan.FromSeconds(positions[1])));
		danmaku.OnReceived(Comment("二"));
		Task<DanmakuSegment?> closed = danmaku.CompleteSegment(first);
		danmaku.OnSegmentStarted(second);
		await closed.WaitAsync(cancellationToken);
		timeline.Next().SetResult(new TimelinePosition(1, second, TimeSpan.FromSeconds(positions[2])));
		danmaku.OnReceived(Comment("三"));
		Task<DanmakuSegment?> last = danmaku.CompleteSegment(second);
		danmaku.OnVideoEnded();
		await last.WaitAsync(cancellationToken);
		(string Text, double Seconds)[] a = await CommentsAsync("a.xml", cancellationToken);
		(string Text, double Seconds)[] b = await CommentsAsync("b.xml", cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(a).IsEquivalentTo(inFirst, CollectionOrdering.Matching);
			await Assert.That(b).IsEquivalentTo(inSecond, CollectionOrdering.Matching);
		}
	}

	// 上一段还开着时下一段的弹幕先压着
	[Test]
	public async Task DanmakuHandedOverFromTheEndOfASegmentComeBeforeTheNextOnesOwnAsync(CancellationToken cancellationToken)
	{
		ControlledTimeline timeline = new() { Duration = SegmentDuration };
		await using RoomDanmaku danmaku = Create();
		string first = _directory.Combine("a.flv");
		string second = _directory.Combine("b.flv");
		danmaku.SetRecording(true);
		danmaku.OnVideoStarted(timeline, first, Run());

		timeline.Next().SetResult(new TimelinePosition(0, first, TimeSpan.FromSeconds(10.5)));
		danmaku.OnReceived(Comment("推过末尾"));
		timeline.Next().SetResult(new TimelinePosition(1, second, TimeSpan.FromSeconds(1)));
		danmaku.OnReceived(Comment("第二段"));
		await CloseSegmentsAsync(danmaku, first, second, cancellationToken);

		await Assert.That(await CommentsAsync("b.xml", cancellationToken)).IsEquivalentTo([("推过末尾", 0.5), ("第二段", 1.0)], CollectionOrdering.Matching);
	}

	// 不能顺延到再后面的一段
	[Test]
	public async Task DanmakuPastTheEndOfASegmentAreLeftOutWhenTheNextSegmentHasNoFileAsync(CancellationToken cancellationToken)
	{
		ListLogger logger = new();
		bool isEnabled = true;
		ControlledTimeline timeline = new() { Duration = static _ => TimeSpan.FromSeconds(10) };
		await using RoomDanmaku danmaku = new(1, _time, logger, static () => { });
		string first = _directory.Combine("a.flv");
		string second = _directory.Combine("b.flv");
		string third = _directory.Combine("c.flv");
		danmaku.SetRecording(true);
		danmaku.OnVideoStarted(timeline, first, Run([SuppressMessage("ReSharper", "AccessToModifiedClosure")] () => isEnabled));
		timeline.Next().SetResult(new TimelinePosition(0, first, TimeSpan.FromSeconds(10.5)));
		danmaku.OnReceived(Comment("推过末尾"));
		await BarrierAsync(danmaku, cancellationToken);

		isEnabled = false;
		danmaku.OnSettingsChanged();
		Task<DanmakuSegment?> closed = danmaku.CompleteSegment(first);
		danmaku.OnSegmentStarted(second);
		await closed.WaitAsync(cancellationToken);
		await danmaku.CompleteSegment(second).WaitAsync(cancellationToken);

		isEnabled = true;
		danmaku.OnSettingsChanged();
		danmaku.OnSegmentStarted(third);
		timeline.Next().SetResult(new TimelinePosition(2, third, TimeSpan.FromSeconds(1)));
		danmaku.OnReceived(Comment("第三段"));
		Task<DanmakuSegment?> last = danmaku.CompleteSegment(third);
		danmaku.OnVideoEnded();
		await last.WaitAsync(cancellationToken);
		await danmaku.DisposeAsync();

		using (Assert.Multiple())
		{
			await Assert.That(await CommentsAsync("c.xml", cancellationToken)).IsEquivalentTo([("第三段", 1.0)], CollectionOrdering.Matching);
			await Assert.That(logger.Entries).Contains((LogLevel.Information, "1 danmaku past the end of a segment were left out because the next segment has no danmaku file"));
		}
	}

	// 从没连上时断开的区间就是整段画面
	[Test]
	[Arguments(2)]
	[Arguments(-2)]
	public async Task AGapCoveringTheWholeSegmentMatchesTheVideoWhateverTheOffsetAsync(int offsetSeconds, CancellationToken cancellationToken)
	{
		await using RoomDanmaku danmaku = Create();
		string first = _directory.Combine("a.flv");
		danmaku.SetRecording(true);
		danmaku.OnVideoStarted(new ControlledTimeline(), first, Run(offset: TimeSpan.FromSeconds(offsetSeconds)));
		Task<DanmakuSegment?> closed = danmaku.CompleteSegment(first);
		danmaku.OnVideoEnded();
		await closed.WaitAsync(cancellationToken);

		await Assert.That(await RecordsAsync("a.xml", cancellationToken)).IsEquivalentTo([new DanmakuRecord.Gap(TimeSpan.Zero, TimeSpan.FromSeconds(30))], EqualityComparer<DanmakuRecord>.Default, CollectionOrdering.Matching);
	}

	// 断开的起点是弹幕的位置，和弹幕一样加偏移；终点是这一段画面的结尾
	[Test]
	[Arguments(2)]
	[Arguments(-2)]
	public async Task AGapLeftOpenEndsWithTheVideoWhateverTheOffsetAsync(int offsetSeconds, CancellationToken cancellationToken)
	{
		ControlledTimeline timeline = new();
		await using RoomDanmaku danmaku = Create();
		string first = _directory.Combine("a.flv");
		TimeSpan offset = TimeSpan.FromSeconds(offsetSeconds);
		danmaku.SetRecording(true);
		danmaku.OnReceived(Connected());
		danmaku.OnReceived(LoginNotice());
		danmaku.OnVideoStarted(timeline, first, Run(offset: offset));
		timeline.Next().SetResult(new TimelinePosition(0, first, TimeSpan.FromSeconds(10)));
		danmaku.OnDisconnected();
		Task<DanmakuSegment?> closed = danmaku.CompleteSegment(first);
		danmaku.OnVideoEnded();
		await closed.WaitAsync(cancellationToken);

		await Assert.That((await RecordsAsync("a.xml", cancellationToken)).OfType<DanmakuRecord.Gap>()).IsEquivalentTo([new DanmakuRecord.Gap(TimeSpan.FromSeconds(10) + offset, TimeSpan.FromSeconds(30))], EqualityComparer<DanmakuRecord.Gap>.Default, CollectionOrdering.Matching);
	}

	// 文件开头的连接状态是画面开头的状态
	[Test]
	[MatrixDataSource]
	public async Task TheConnectionAtTheStartOfAFileIsAtZeroWhateverTheOffsetAsync([Matrix(3, -3)] int offsetSeconds, [Matrix] bool isSettledBeforeTheFile, CancellationToken cancellationToken)
	{
		await using RoomDanmaku danmaku = Create();
		string first = _directory.Combine("a.flv");
		danmaku.SetRecording(true);
		danmaku.OnReceived(Connected());

		if (isSettledBeforeTheFile)
		{
			danmaku.OnReceived(LoginNotice());
		}

		danmaku.OnVideoStarted(new ControlledTimeline(), first, Run(offset: TimeSpan.FromSeconds(offsetSeconds)));

		if (!isSettledBeforeTheFile)
		{
			danmaku.OnReceived(LoginNotice());
		}

		Task<DanmakuSegment?> closed = danmaku.CompleteSegment(first);
		danmaku.OnVideoEnded();
		await closed.WaitAsync(cancellationToken);

		await Assert.That((await RecordsAsync("a.xml", cancellationToken)).OfType<DanmakuRecord.Connection>()).IsEquivalentTo([new DanmakuRecord.Connection(TimeSpan.Zero, true, true)], EqualityComparer<DanmakuRecord.Connection>.Default, CollectionOrdering.Matching);
	}

	// 录制结束时还没等到通知的文件，开头按未打码记下连接
	[Test]
	[Arguments(3)]
	[Arguments(-3)]
	public async Task AConnectionStillAwaitedWhenRecordingEndsIsAtZeroWhateverTheOffsetAsync(int offsetSeconds, CancellationToken cancellationToken)
	{
		RoomDanmaku danmaku = Create();
		danmaku.SetRecording(true);
		danmaku.OnReceived(Connected());
		danmaku.OnVideoStarted(new ControlledTimeline(), _directory.Combine("a.flv"), Run(offset: TimeSpan.FromSeconds(offsetSeconds)));
		await danmaku.DisposeAsync();

		await Assert.That((await RecordsAsync("a.xml", cancellationToken)).OfType<DanmakuRecord.Connection>()).IsEquivalentTo([new DanmakuRecord.Connection(TimeSpan.Zero, true, false)], EqualityComparer<DanmakuRecord.Connection>.Default, CollectionOrdering.Matching);
	}

	// 改写偏移靠它避开正在写的文件
	[Test]
	public async Task OpenFileIsReportedAsWritingUntilTheSegmentClosesAsync(CancellationToken cancellationToken)
	{
		await using RoomDanmaku danmaku = Create();
		string video = _directory.Combine("a.flv");
		string xml = _directory.Combine("a.xml");
		danmaku.SetRecording(true);
		danmaku.OnVideoStarted(new ControlledTimeline(), video, Run());

		await BarrierAsync(danmaku, cancellationToken);
		bool whileOpen = danmaku.IsWriting(xml);

		DanmakuSegment? segment = await danmaku.CompleteSegment(video).WaitAsync(cancellationToken);

		await Assert.That((whileOpen, segment?.Path, danmaku.IsWriting(xml))).IsEqualTo((true, xml, false));
	}

	// 开着保存原始消息也一样，不建原始消息文件，也不解析原始消息
	[Test]
	public async Task SegmentWithoutDanmakuRecordingHasNoFileAsync(CancellationToken cancellationToken)
	{
		await using RoomDanmaku danmaku = Create();
		string video = _directory.Combine("a.flv");
		danmaku.OnVideoStarted(new ControlledTimeline(), video, Run(static () => false, saveRaw: true));

		DanmakuSegment? segment = await danmaku.CompleteSegment(video).WaitAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(segment).IsNull();
			await Assert.That(Directory.GetFiles(_directory.Root)).IsEmpty();
			await Assert.That(danmaku.Options.IncludeRaw).IsFalse();
		}
	}

	[Test]
	public async Task MessagesBeyondTheQueueLimitAreCountedAsDroppedAsync(CancellationToken cancellationToken)
	{
		ControlledTimeline timeline = new();
		await using RoomDanmaku danmaku = Create();
		string video = _directory.Combine("a.flv");
		danmaku.SetRecording(true);
		danmaku.OnVideoStarted(timeline, video, Run());

		// 写入者卡在第一条的归属上
		TaskCompletionSource<TimelinePosition?> stuck = timeline.Next();
		danmaku.OnReceived(Comment("第一条"));
		await timeline.Placing.Task.WaitAsync(cancellationToken);

		for (int i = 0; i < RoomDanmaku.QueueCapacity + 5; ++i)
		{
			danmaku.OnReceived(Comment("刷屏"));
		}

		long dropped = danmaku.DroppedMessages;
		timeline.Fallback = new TimelinePosition(0, video, TimeSpan.Zero);
		stuck.SetResult(null);
		await danmaku.CompleteSegment(video).WaitAsync(cancellationToken);

		await Assert.That(dropped).IsEqualTo(5);
	}

	// 开播时房间开始记录常常落在认证和未登录通知之间；没等到通知就有弹幕时按未打码记下连接
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task AFileStartedBeforeTheLoginNoticeGetsOneConnectionAsync(bool isNoticed, CancellationToken cancellationToken)
	{
		await using RoomDanmaku danmaku = Create();
		danmaku.SetRecording(true);
		danmaku.OnReceived(Connected());
		danmaku.StartTextOnly(Run());

		if (isNoticed)
		{
			danmaku.OnReceived(LoginNotice());
		}

		danmaku.OnReceived(Comment("弹幕"));
		danmaku.StopTextOnly();
		await danmaku.DisposeAsync();
		DanmakuFile file = await ReadTextOnlyFileAsync(cancellationToken);

		await Assert.That(file.Records).IsEquivalentTo([new DanmakuRecord.Connection(TimeSpan.Zero, true, isNoticed), RecordedComment(TimeSpan.Zero, "弹幕")], EqualityComparer<DanmakuRecord>.Default, CollectionOrdering.Matching);
	}

	// 队列的可读通知是异步送达的，通知排进队列时等待可能已经先到期
	[Test]
	public async Task ALoginNoticeQueuedBeforeTheDeadlineCountsEvenIfTheWaitEndsFirstAsync(CancellationToken cancellationToken)
	{
		ManualReader reader = new();
		DanmakuRecorder recorder = new(1, _time, NullLogger.Instance, new DanmuReceiveOptions(), static _ => { });
		reader.Add(new DanmakuWork.TextOnlyStarted(Run(), _time.GetTimestamp()));
		reader.Add(new DanmakuWork.Received(Connected()));
		Task running = recorder.RunAsync(reader, static _ => { }, cancellationToken);

		// 写入者开始等通知：可读通知不会来，只有到期能把它叫醒
		await _time.TimerCreated.Task.WaitAsync(cancellationToken);
		reader.Add(new DanmakuWork.Received(LoginNotice()));
		_time.Advance(DanmakuRecorder.LoginNoticeWait * 2);
		reader.Add(new DanmakuWork.TextOnlyStopped());
		reader.Complete();
		await running.WaitAsync(cancellationToken);
		DanmakuFile file = await ReadTextOnlyFileAsync(cancellationToken);

		await Assert.That(file.Records.OfType<DanmakuRecord.Connection>()).IsEquivalentTo([new DanmakuRecord.Connection(TimeSpan.Zero, true, true)], EqualityComparer<DanmakuRecord.Connection>.Default, CollectionOrdering.Matching);
	}

	[Test]
	public async Task AFileThatCannotBeWrittenIsGivenUpWithoutRetryingAsync(CancellationToken cancellationToken)
	{
		ListLogger logger = new();
		TaskCompletionSource failed = new(TaskCreationOptions.RunContinuationsAsynchronously);
		HookedStream stream = new(index =>
		{
			if (index > 1)
			{
				failed.TrySetResult();
				throw new IOException("磁盘已满");
			}

			return Task.CompletedTask;
		});
		bool isRecording = false;
		DanmakuRecorder recorder = new(1, _time, logger, new DanmuReceiveOptions(), recording => isRecording = recording)
		{
			CreateWriterAsync = (_, header, token) => DanmakuXmlWriter.CreateAsync(stream, header, DanmakuFlushPolicy.Default, _time, token)
		};
		Channel<DanmakuWork> queue = Channel.CreateUnbounded<DanmakuWork>();
		queue.Writer.TryWrite(new DanmakuWork.TextOnlyStarted(Run(), _time.GetTimestamp()));
		queue.Writer.TryWrite(new DanmakuWork.Received(Comment("写不进去")));
		Task running = Task.Run(() => recorder.RunAsync(queue.Reader, static _ => { }, cancellationToken), cancellationToken);

		// 写入者开始等按时刷新，到期后写失败；处理完失败、回去等待之后再过一个间隔也不该重试
		await _time.TimerCreated.Task.WaitAsync(cancellationToken);
		_time.Advance(DanmakuFlushPolicy.Default.Interval);
		await failed.Task.WaitAsync(cancellationToken);
		await BarrierAsync(queue.Writer, cancellationToken);
		_time.Advance(DanmakuFlushPolicy.Default.Interval);
		await BarrierAsync(queue.Writer, cancellationToken);
		int writes = stream.Writes;
		bool recordingAfterFailure = isRecording;

		queue.Writer.TryWrite(new DanmakuWork.TextOnlyStopped());
		queue.Writer.Complete();
		await running.WaitAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(writes).IsEqualTo(2).Because("失败之后不再按时重试");
			await Assert.That(recordingAfterFailure).IsFalse();
			await Assert.That(stream.Writes).IsEqualTo(3).Because("关闭时再写一次");
			await Assert.That(logger.Entries.Where(static entry => entry.Level is LogLevel.Warning)).Count().IsEqualTo(1).Because("仍然失败也只记一条警告");
		}
	}

	// 写入者停下的话队列不会完成，之后关闭段的请求只能等到超时
	[Test]
	public async Task AScheduledFlushThatThrowsDoesNotStopTheWriterAsync(CancellationToken cancellationToken)
	{
		ListLogger logger = new();
		ControlledTimeline timeline = new();
		await using RoomDanmaku danmaku = new(1, _time, logger, static () => { });
		string video = _directory.Combine("a.flv");
		danmaku.SetRecording(true);
		danmaku.OnVideoStarted(timeline, video, Run());
		danmaku.OnReceived(Connected());

		// 等不到未登录通知，到期时放置连接记录出错
		await _time.TimerCreated.Task.WaitAsync(cancellationToken);
		timeline.Next().SetException(new InvalidOperationException());
		_time.Advance(DanmakuRecorder.LoginNoticeWait);
		await timeline.Placing.Task.WaitAsync(cancellationToken);
		DanmakuSegment? segment = await danmaku.CompleteSegment(video).WaitAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(segment?.Path).IsEqualTo(_directory.Combine("a.xml"));
			await Assert.That(logger.Entries).Contains((LogLevel.Warning, "Failed to flush danmaku on schedule"));
		}
	}

	// 关闭时从原位置重写，盖掉半条记录
	[Test]
	public async Task AFileThatFailedHalfwayIsCompletedWhenClosedAsync(CancellationToken cancellationToken)
	{
		ListLogger logger = new();
		HalfWrittenOnceStream stream = new(failAt: 2);
		DanmakuRecorder recorder = new(1, _time, logger, new DanmuReceiveOptions(), static _ => { })
		{
			CreateWriterAsync = (_, header, token) => DanmakuXmlWriter.CreateAsync(stream, header, DanmakuFlushPolicy.Default, _time, token)
		};
		Channel<DanmakuWork> queue = Channel.CreateUnbounded<DanmakuWork>();
		queue.Writer.TryWrite(new DanmakuWork.TextOnlyStarted(Run(), _time.GetTimestamp()));
		queue.Writer.TryWrite(new DanmakuWork.Received(Comment("写到一半")));
		Task running = Task.Run(() => recorder.RunAsync(queue.Reader, static _ => { }, cancellationToken), cancellationToken);

		await _time.TimerCreated.Task.WaitAsync(cancellationToken);
		_time.Advance(DanmakuFlushPolicy.Default.Interval);
		await stream.Failed.Task.WaitAsync(cancellationToken);
		queue.Writer.TryWrite(new DanmakuWork.TextOnlyStopped());
		queue.Writer.Complete();
		await running.WaitAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(DanmakuSamples.ParseStrictly(stream.ToArray()).GetValueOrDefault(@"d")).IsEqualTo(1);
			await Assert.That(logger.Entries.Where(static entry => entry.Level is LogLevel.Warning)).Count().IsEqualTo(1);
		}
	}

	[Test]
	public async Task AFileCountsAsBeingWrittenFromCreationUntilItIsClosedAsync(CancellationToken cancellationToken)
	{
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
		DanmakuRecorder recorder = new(1, _time, NullLogger.Instance, new DanmuReceiveOptions(), static _ => { })
		{
			CreateWriterAsync = (created, header, token) =>
			{
				path = created;
				return DanmakuXmlWriter.CreateAsync(stream, header, DanmakuFlushPolicy.Default, _time, token);
			}
		};
		Channel<DanmakuWork> queue = Channel.CreateUnbounded<DanmakuWork>();
		queue.Writer.TryWrite(new DanmakuWork.TextOnlyStarted(Run(), _time.GetTimestamp()));
		Task running = Task.Run(() => recorder.RunAsync(queue.Reader, static _ => { }, cancellationToken), cancellationToken);

		await headerBlocked.Task.WaitAsync(cancellationToken);
		bool whileCreating = path is not null && recorder.IsWriting(path);
		headerGate.SetResult();

		queue.Writer.TryWrite(new DanmakuWork.Received(Comment("最后一条")));
		queue.Writer.TryWrite(new DanmakuWork.TextOnlyStopped());
		await closeBlocked.Task.WaitAsync(cancellationToken);
		bool whileClosing = path is not null && recorder.IsWriting(path);
		closeGate.SetResult();

		queue.Writer.Complete();
		await running.WaitAsync(cancellationToken);

		await Assert.That((whileCreating, whileClosing, path is not null && recorder.IsWriting(path))).IsEqualTo((true, true, false));

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
		await using RoomDanmaku danmaku = Create();
		IDisposable subscription = danmaku.Subscribe();
		danmaku.OnReceived(Comment("看着呢"));
		subscription.Dispose();
		danmaku.OnReceived(Comment("这一帧剩下的"));
		using IDisposable again = danmaku.Subscribe();

		await Assert.That(danmaku.Feed.GetEntries(0)).IsEmpty();
	}

	// 重连落在下一段、段开始的通知还没到时，当场建出的下一段文件开头是断开的；前一段断开到它的末尾
	[Test]
	public async Task AReconnectionThatOpensTheNextSegmentStartsItDisconnectedAsync(CancellationToken cancellationToken)
	{
		ControlledTimeline timeline = new();
		await using RoomDanmaku danmaku = Create();
		string first = _directory.Combine("a.flv");
		string second = _directory.Combine("b.flv");
		danmaku.SetRecording(true);
		danmaku.OnReceived(Connected());
		danmaku.OnReceived(LoginNotice());
		danmaku.OnVideoStarted(timeline, first, Run());
		timeline.Next().SetResult(new TimelinePosition(0, first, TimeSpan.FromSeconds(10)));
		danmaku.OnDisconnected();
		danmaku.OnReceived(Connected());
		timeline.Next().SetResult(new TimelinePosition(1, second, TimeSpan.FromSeconds(5)));
		danmaku.OnReceived(LoginNotice());
		await CloseSegmentsAsync(danmaku, first, second, cancellationToken);
		IReadOnlyList<DanmakuRecord> a = await RecordsAsync("a.xml", cancellationToken);
		IReadOnlyList<DanmakuRecord> b = await RecordsAsync("b.xml", cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(a).IsEquivalentTo([new DanmakuRecord.Connection(TimeSpan.Zero, true, true), new DanmakuRecord.Gap(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30))], EqualityComparer<DanmakuRecord>.Default, CollectionOrdering.Matching);
			await Assert.That(b).IsEquivalentTo([new DanmakuRecord.Gap(TimeSpan.Zero, TimeSpan.FromSeconds(5)), new DanmakuRecord.Connection(TimeSpan.FromSeconds(5), true, true)], EqualityComparer<DanmakuRecord>.Default, CollectionOrdering.Matching);
		}
	}

	// 断开落在下一段、段开始的通知还没到时，当场建出的下一段文件开头是连着的，从断开的位置起算断开
	[Test]
	public async Task ADisconnectionThatOpensTheNextSegmentStartsItConnectedAsync(CancellationToken cancellationToken)
	{
		ControlledTimeline timeline = new();
		await using RoomDanmaku danmaku = Create();
		string first = _directory.Combine("a.flv");
		string second = _directory.Combine("b.flv");
		danmaku.SetRecording(true);
		danmaku.OnReceived(Connected());
		danmaku.OnReceived(LoginNotice());
		danmaku.OnVideoStarted(timeline, first, Run());
		timeline.Next().SetResult(new TimelinePosition(1, second, TimeSpan.FromSeconds(3)));
		danmaku.OnDisconnected();
		await CloseSegmentsAsync(danmaku, first, second, cancellationToken);

		await Assert.That(await RecordsAsync("b.xml", cancellationToken)).IsEquivalentTo([new DanmakuRecord.Connection(TimeSpan.Zero, true, true), new DanmakuRecord.Gap(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(30))], EqualityComparer<DanmakuRecord>.Default, CollectionOrdering.Matching);
	}

	[Test]
	public async Task ALoginNoticeAfterTheConnectionIsRecordedAddsAMaskedConnectionAsync(CancellationToken cancellationToken)
	{
		await using RoomDanmaku danmaku = Create();
		danmaku.SetRecording(true);
		danmaku.StartTextOnly(Run());
		danmaku.OnReceived(Connected());
		danmaku.OnReceived(Comment("通知之前"));
		_time.Advance(TimeSpan.FromSeconds(1));
		danmaku.OnReceived(LoginNotice());
		danmaku.StopTextOnly();
		await danmaku.DisposeAsync();
		DanmakuFile file = await ReadTextOnlyFileAsync(cancellationToken);

		await Assert.That(file.Records).IsEquivalentTo([new DanmakuRecord.Connection(TimeSpan.Zero, true, false), RecordedComment(TimeSpan.Zero, "通知之前"), new DanmakuRecord.Connection(TimeSpan.FromSeconds(1), true, true)], EqualityComparer<DanmakuRecord>.Default, CollectionOrdering.Matching);
	}

	// 后处理拿到的是整段的时长
	[Test]
	public async Task TurningDanmakuOffAndOnWithinASegmentKeepsWritingTheSameFileAsync(CancellationToken cancellationToken)
	{
		ListLogger logger = new();
		bool isEnabled = true;
		TimeSpan duration = TimeSpan.FromSeconds(10);
		string video = _directory.Combine("a.flv");
		ControlledTimeline timeline = new() { Fallback = new TimelinePosition(0, video, TimeSpan.FromSeconds(1)), Duration = [SuppressMessage("ReSharper", "AccessToModifiedClosure")] (_) => duration };
		await using RoomDanmaku danmaku = new(1, _time, logger, static () => { });
		danmaku.SetRecording(true);
		danmaku.OnVideoStarted(timeline, video, Run([SuppressMessage("ReSharper", "AccessToModifiedClosure")] () => isEnabled));
		danmaku.OnReceived(Comment("关之前"));
		await BarrierAsync(danmaku, cancellationToken);

		isEnabled = false;
		danmaku.OnSettingsChanged();
		danmaku.OnReceived(Comment("关着"));
		await BarrierAsync(danmaku, cancellationToken);
		bool recordingWhileOff = danmaku.Snapshot.IsRecording;

		isEnabled = true;
		danmaku.OnSettingsChanged();
		danmaku.OnReceived(Comment("又打开"));
		await BarrierAsync(danmaku, cancellationToken);
		duration = TimeSpan.FromSeconds(20);
		DanmakuSegment? segment = await danmaku.CompleteSegment(video).WaitAsync(cancellationToken);
		danmaku.OnVideoEnded();
		await danmaku.DisposeAsync();
		(string Text, double Seconds)[] comments = await CommentsAsync("a.xml", cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(comments.Select(static comment => comment.Text)).IsEquivalentTo(["关之前", "又打开"], CollectionOrdering.Matching);
			await Assert.That(segment?.Duration).IsEqualTo(TimeSpan.FromSeconds(20));
			await Assert.That(recordingWhileOff).IsFalse();
			await Assert.That(logger.Entries.Where(static entry => entry.Message.Contains("after their file was closed", StringComparison.Ordinal))).IsEmpty();
		}
	}

	// 段中途才打开录弹幕时文件才建，文件里的时间仍从画面开头算
	[Test]
	public async Task AFileOpenedMidwayRecordsWhenItsVideoStartedAsync(CancellationToken cancellationToken)
	{
		bool isEnabled = false;
		ControlledTimeline timeline = new();
		await using RoomDanmaku danmaku = Create();
		string video = _directory.Combine("a.flv");
		DateTimeOffset videoStart = _time.GetLocalNow();
		danmaku.SetRecording(true);
		danmaku.OnVideoStarted(timeline, video, Run([SuppressMessage("ReSharper", "AccessToModifiedClosure")] () => isEnabled));
		await BarrierAsync(danmaku, cancellationToken);

		_time.Advance(TimeSpan.FromMinutes(10));
		isEnabled = true;
		danmaku.OnSettingsChanged();
		timeline.Next().SetResult(new TimelinePosition(0, video, TimeSpan.FromMinutes(10)));
		danmaku.OnReceived(Comment("打开之后"));
		await danmaku.CompleteSegment(video).WaitAsync(cancellationToken);
		DanmakuFile file = await DanmakuXmlReader.ReadFileAsync(_directory.Combine("a.xml"), cancellationToken);

		await Assert.That(file.Source).IsTypeOf<DanmakuFileSource.Native>().And.Member(static source => source.Header.RecordStartTime, start => start.IsEqualTo(videoStart));
	}

	// 写入者卡在放置前面的弹幕上时，新一段的开头仍是换文件的时刻
	[Test]
	public async Task ASegmentStartedWhileTheWriterIsBusyRecordsWhenItStartedAsync(CancellationToken cancellationToken)
	{
		ControlledTimeline timeline = new();
		await using RoomDanmaku danmaku = Create();
		string first = _directory.Combine("a.flv");
		string second = _directory.Combine("b.flv");
		danmaku.SetRecording(true);
		danmaku.OnVideoStarted(timeline, first, Run());

		TaskCompletionSource<TimelinePosition?> pending = timeline.Next();
		danmaku.OnReceived(Comment("还在等"));
		await timeline.Placing.Task.WaitAsync(cancellationToken);
		DateTimeOffset secondStart = _time.GetLocalNow();
		Task<DanmakuSegment?> closed = danmaku.CompleteSegment(first);
		danmaku.OnSegmentStarted(second);
		_time.Advance(TimeSpan.FromSeconds(5));
		pending.SetResult(new TimelinePosition(0, first, TimeSpan.FromSeconds(3)));
		await closed.WaitAsync(cancellationToken);
		await danmaku.CompleteSegment(second).WaitAsync(cancellationToken);
		DanmakuFile file = await DanmakuXmlReader.ReadFileAsync(_directory.Combine("b.xml"), cancellationToken);

		await Assert.That(file.Source).IsTypeOf<DanmakuFileSource.Native>().And.Member(static source => source.Header.RecordStartTime, start => start.IsEqualTo(secondStart));
	}

	// 又打开时开头补记当前的连接
	[Test]
	public async Task RawMessagesStopWhileDanmakuIsOffAndContinueInTheSameFileAsync(CancellationToken cancellationToken)
	{
		bool isEnabled = true;
		string video = _directory.Combine("a.flv");
		ControlledTimeline timeline = new() { Fallback = new TimelinePosition(0, video, TimeSpan.FromSeconds(1)) };
		await using RoomDanmaku danmaku = Create();
		string first = UnixMilliseconds();
		danmaku.SetRecording(true);
		danmaku.OnVideoStarted(timeline, video, Run([SuppressMessage("ReSharper", "AccessToModifiedClosure")] () => isEnabled, saveRaw: true));
		danmaku.OnReceived(Connected());
		danmaku.OnReceived(LoginNotice());
		await BarrierAsync(danmaku, cancellationToken);
		bool rawWhileOn = danmaku.Options.IncludeRaw;

		isEnabled = false;
		danmaku.OnSettingsChanged();
		await BarrierAsync(danmaku, cancellationToken);
		bool rawWhileOff = danmaku.Options.IncludeRaw;
		string raw = await Assert.That(Directory.GetFiles(_directory.Root, "*.jsonl")).HasSingleItem();
		long lengthWhileOff = new FileInfo(raw).Length;
		_time.Advance(TimeSpan.FromSeconds(1));
		string second = UnixMilliseconds();
		danmaku.OnDisconnected();
		danmaku.OnReceived(Connected());
		danmaku.OnReceived(LoginNotice());
		await BarrierAsync(danmaku, cancellationToken);
		long lengthAfterReconnecting = new FileInfo(raw).Length;

		isEnabled = true;
		danmaku.OnSettingsChanged();
		danmaku.OnReceived(Raw("DANMU_MSG"));
		await danmaku.CompleteSegment(video).WaitAsync(cancellationToken);
		danmaku.OnVideoEnded();
		danmaku.EndSession();
		await danmaku.DisposeAsync();
		string[] lines = await File.ReadAllLinesAsync(raw, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That((rawWhileOn, rawWhileOff)).IsEqualTo((true, false));
			await Assert.That(lengthAfterReconnecting).IsEqualTo(lengthWhileOff);
			await Assert.That(Directory.GetFiles(_directory.Root, "*.jsonl")).IsEquivalentTo([raw]);
			await Assert.That(lines).IsEquivalentTo(["{\"t\":" + first + ",\"cmd\":\"@connected\",\"raw\":{\"anonymous\":true}}", "{\"t\":" + second + ",\"cmd\":\"@connected\",\"raw\":{\"anonymous\":true,\"masked\":true}}", "{\"t\":" + second + ",\"cmd\":\"DANMU_MSG\",\"raw\":{\"cmd\":\"DANMU_MSG\"}}"], CollectionOrdering.Matching);
		}
	}

	// 补记的时刻是连接建立的时刻
	[Test]
	public async Task RawMessagesStartedBeforeTheConnectionIsSettledRecordItOnceSettledAsync(CancellationToken cancellationToken)
	{
		await using RoomDanmaku danmaku = Create();
		string connectedAt = UnixMilliseconds();
		danmaku.SetRecording(true);
		danmaku.OnReceived(Connected());
		_time.Advance(TimeSpan.FromSeconds(1));
		danmaku.StartTextOnly(Run(saveRaw: true));
		danmaku.OnReceived(LoginNotice());
		danmaku.StopTextOnly();
		danmaku.EndSession();
		await danmaku.DisposeAsync();
		string raw = await Assert.That(Directory.GetFiles(_directory.Root, "*.jsonl", SearchOption.AllDirectories)).HasSingleItem();

		await Assert.That(await File.ReadAllLinesAsync(raw, cancellationToken)).IsEquivalentTo(["{\"t\":" + connectedAt + ",\"cmd\":\"@connected\",\"raw\":{\"anonymous\":true,\"masked\":true}}"], CollectionOrdering.Matching);
	}

	// 写不进去时停到下一个文件
	[Test]
	public async Task RawMessagesContinueInTheSameFileAfterAWriteFailureAsync(CancellationToken cancellationToken)
	{
		DanmuReceiveOptions options = new();
		int opened = 0;
		bool rawAfterFailure = true;
		DanmakuRecorder recorder = new(1, _time, NullLogger.Instance, options, static _ => { })
		{
			OpenRawWriter = path => ++opened is 1 ? new RawDanmakuWriter(new HookedStream(static _ => throw new IOException("磁盘已满")), new DanmakuFlushPolicy(TimeSpan.FromSeconds(5), 1), _time) : RawDanmakuWriter.Open(path, DanmakuFlushPolicy.Default, _time)
		};
		Channel<DanmakuWork> queue = Channel.CreateUnbounded<DanmakuWork>();
		queue.Writer.TryWrite(new DanmakuWork.TextOnlyStarted(Run(saveRaw: true, splitOnTitleChange: true), _time.GetTimestamp()));
		queue.Writer.TryWrite(new DanmakuWork.Received(Raw("FAILED")));
		queue.Writer.TryWrite(new DanmakuWork.Received(Raw("DROPPED")));
		queue.Writer.TryWrite(new DanmakuWork.TitleChanged("new title", _time.GetTimestamp()));
		queue.Writer.TryWrite(new DanmakuWork.Received(Raw("WRITTEN")));
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

		string firstFile = await FindFileTitledAsync("title", cancellationToken);
		string raw = await Assert.That(Directory.GetFiles(_directory.Root, "*.jsonl", SearchOption.AllDirectories)).HasSingleItem();
		string[] lines = await File.ReadAllLinesAsync(raw, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That((opened, rawAfterFailure)).IsEqualTo((2, false));
			await Assert.That(raw).IsEqualTo(RecordingFileLayout.GetStem(firstFile) + RecordingFileLayout.RawDanmakuExtension);
			await Assert.That(lines).IsEquivalentTo(["{\"t\":" + UnixMilliseconds() + ",\"cmd\":\"WRITTEN\",\"raw\":{\"cmd\":\"WRITTEN\"}}"], CollectionOrdering.Matching);
		}
	}

	private static TimeSpan? SegmentDuration(string path)
	{
		return TimeSpan.FromSeconds(path.EndsWith("a.flv", StringComparison.Ordinal) ? 10 : 5);
	}

	// Comment 写进文件再读回的样子
	private static DanmakuRecord.Comment RecordedComment(TimeSpan time, string text)
	{
		return new DanmakuRecord.Comment(time, 1, 25, 0xFFFFFF, DateTimeOffset.UnixEpoch, 0, "观众", text, 0);
	}

	private RoomDanmaku Create()
	{
		return new RoomDanmaku(1, _time, NullLogger.Instance, static () => { });
	}

	// 每次开文件或设置变化时读一次 isEnabled
	private DanmakuRun Run(Func<bool>? isEnabled = null, bool saveRaw = false, bool splitOnTitleChange = false, TimeSpan offset = default)
	{
		return new DanmakuRun(static () => new DanmakuFileHeader { RoomId = 1, Title = "title" }, _ => ValueTask.FromResult(new DanmakuRunSettings(new DanmakuProfile(isEnabled?.Invoke() ?? true, true, DanmakuGiftMode.None, true, offset, true, true, saveRaw, false), _directory.Root, false, splitOnTitleChange)));
	}

	// 写入者按顺序处理，这个关闭请求有了结果就说明之前的工作都已处理完
	private async Task BarrierAsync(RoomDanmaku danmaku, CancellationToken cancellationToken)
	{
		await danmaku.CompleteSegment(_directory.Combine("barrier.flv")).WaitAsync(cancellationToken);
	}

	private async Task BarrierAsync(ChannelWriter<DanmakuWork> queue, CancellationToken cancellationToken)
	{
		TaskCompletionSource<DanmakuSegment?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
		queue.TryWrite(new DanmakuWork.SegmentCompleted(_directory.Combine("barrier.flv"), completion));
		await completion.Task.WaitAsync(cancellationToken);
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

	private async Task<IReadOnlyList<DanmakuRecord>> RecordsAsync(string name, CancellationToken cancellationToken)
	{
		return (await DanmakuXmlReader.ReadFileAsync(_directory.Combine(name), cancellationToken)).Records;
	}

	private async Task<(string Text, double Seconds)[]> CommentsAsync(string name, CancellationToken cancellationToken)
	{
		return [.. (await RecordsAsync(name, cancellationToken)).OfType<DanmakuRecord.Comment>().Select(static comment => (comment.Text, comment.Time.TotalSeconds))];
	}

	// 仅弹幕的文件按开始时间命名，放在房间目录里
	private async Task<DanmakuFile> ReadTextOnlyFileAsync(CancellationToken cancellationToken)
	{
		string path = await Assert.That(Directory.GetFiles(_directory.Root, "*" + RecordingFileLayout.DanmakuExtension, SearchOption.AllDirectories)).HasSingleItem();
		return await DanmakuXmlReader.ReadFileAsync(path, cancellationToken);
	}

	private async Task<string> FindFileTitledAsync(string title, CancellationToken cancellationToken)
	{
		foreach (string path in Directory.GetFiles(_directory.Root, "*" + RecordingFileLayout.DanmakuExtension, SearchOption.AllDirectories))
		{
			if ((await DanmakuXmlReader.ReadFileAsync(path, cancellationToken)).Source is DanmakuFileSource.Native { Header.Title: var found } && found == title)
			{
				return path;
			}
		}

		throw new FileNotFoundException(title);
	}

	private string UnixMilliseconds()
	{
		return _time.GetUtcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
	}

	private DanmuReceiveTime ReceivedNow()
	{
		return new DanmuReceiveTime(_time.GetTimestamp(), _time.GetUtcNow());
	}

	private DanmuMessage.Connected Connected()
	{
		return new DanmuMessage.Connected(0) { ReceivedAt = ReceivedNow() };
	}

	private DanmuMessage.LoginNotice LoginNotice()
	{
		return new DanmuMessage.LoginNotice() { ReceivedAt = ReceivedNow() };
	}

	private DanmuMessage.Raw Raw(string command)
	{
		return new DanmuMessage.Raw(command, Encoding.UTF8.GetBytes($$"""{"cmd":"{{command}}"}""")) { ReceivedAt = ReceivedNow() };
	}

	private DanmuMessage.Comment Comment(string text)
	{
		return new DanmuMessage.Comment(new DanmuSender(0, "观众", GuardLevel.None, null), text, 1, 25, 0xFFFFFF, DateTimeOffset.UnixEpoch, "0", false) { ReceivedAt = ReceivedNow() };
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

	private sealed class ControlledTimeline : IRecordingTimeline
	{
		private readonly Queue<TaskCompletionSource<TimelinePosition?>> _results = new();
		private readonly Lock _lock = new();

		public TimelinePosition? Fallback { get; set; }

		public TaskCompletionSource Placing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Func<string, TimeSpan?> Duration { get; init; } = static _ => TimeSpan.FromSeconds(30);

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

		public TaskCompletionSource Failed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
		{
			if (Interlocked.Increment(ref _writes) == failAt)
			{
				await base.WriteAsync(buffer.Slice(0, buffer.Length / 2), cancellationToken);
				Failed.TrySetResult();
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
