using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Recording.Splitting;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using System.Buffers;
using System.IO.Pipelines;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class RecordingSegmenterTest
{
	private static readonly RecordingSplit OneMinute = new RecordingSplit.ByDuration(TimeSpan.FromMinutes(1));

	[Test]
	public async Task SizeLimitSwitchesFilesBeforeAUnitThatWouldNotFitAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();

		// 头 3 字节，每个单元 30 字节，一段最多放得下头加 3 个单元
		await recording.RunAsync(new FakeSplitter(), new RecordingSplit.BySize(100), static async session =>
		{
			foreach (char marker in "abcdefg")
			{
				await session.WriteAsync(FakeSplitter.Unit(marker.ToString(), 30));
			}
		}, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(FakeSplitter.Describe(recording.Files)).IsEqualTo("abc | H0:def | H0:g");
			await Assert.That(recording.Files).All(static file => file.Length <= 100);
			await Assert.That(recording.Starts.Select(static start => start.Reason)).IsEquivalentTo([SplitReason.Size, SplitReason.Size], CollectionOrdering.Matching);
			await Assert.That(recording.Completed).IsEquivalentTo(["0", "1"], CollectionOrdering.Matching);
		}
	}

	[Test]
	[Arguments(true, "abc | H0:def | H0:g")]
	[Arguments(false, "a | H0:b | H0:c | H0:d | H0:e | H0:f | H0:g")]
	public async Task SizeLimitCountsTheBytesLeftAfterRemovingFillerAsync(bool removeFiller, string expected, CancellationToken cancellationToken)
	{
		SplitRecording recording = new();

		// 每个单元 30 字节，其中一半是填充；去掉之后头 3 字节加 3 个单元正好放得下
		await recording.RunAsync(new FakeSplitter(), SplitRecording.Fixed(new RecordingSplit.BySize(50), removeFiller: removeFiller), static async session =>
		{
			foreach (char marker in "abcdefg")
			{
				await session.WriteAsync(FakeSplitter.Unit(marker.ToString(), 30, 15));
			}
		}, cancellationToken);

		using (Assert.Multiple())
		{
			// 换文件的位置按写出的字节记，偏一个字节都会把元素切开
			await Assert.That(FakeSplitter.Describe(recording.Files)).IsEqualTo(expected);
			await Assert.That(recording.Files).All(static file => file.Length <= 50);
			await Assert.That(recording.Files.Sum(static file => file.Count(static value => value is (byte)'F'))).IsEqualTo(removeFiller ? 0 : 7 * 15);
		}
	}

	[Test]
	public async Task UnitLargerThanTheLimitTakesAFileOfItsOwnAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();

		await recording.RunAsync(new FakeSplitter(), new RecordingSplit.BySize(100), static async session =>
		{
			await session.WriteAsync(FakeSplitter.Unit("a", 30));
			await session.WriteAsync(FakeSplitter.Unit("b", 150));
			await session.WriteAsync(FakeSplitter.Unit("c", 30));
		}, cancellationToken);

		await Assert.That(FakeSplitter.Describe(recording.Files)).IsEqualTo("a | H0:b | H0:c");
	}

	[Test]
	public async Task DurationSwitchesFilesAtTheFirstSplitPointAfterItRunsOutAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();
		DateTimeOffset start = recording.Time.GetUtcNow();

		await recording.RunAsync(new FakeSplitter(), OneMinute, static async session =>
		{
			await session.WriteAsync(FakeSplitter.Unit("a", 10));
			session.Advance(TimeSpan.FromSeconds(30));
			await session.WriteAsync(FakeSplitter.Unit("b", 10));
			session.Advance(TimeSpan.FromSeconds(29));
			await session.WriteAsync(FakeSplitter.Data("x", 10));
			session.Advance(TimeSpan.FromSeconds(2));
			await session.WriteAsync(FakeSplitter.Data("y", 10));
			await session.WriteAsync(FakeSplitter.Unit("c", 10));
			session.Advance(TimeSpan.FromSeconds(30));
			await session.WriteAsync(FakeSplitter.Unit("d", 10));
			session.Advance(TimeSpan.FromSeconds(31));
			await session.WriteAsync(FakeSplitter.Unit("e", 10));
			await session.WriteAsync(FakeSplitter.Unit("f", 10));
		}, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(FakeSplitter.Describe(recording.Files)).IsEqualTo("abxy | H0:cd | H0:ef");
			await Assert.That(recording.Starts.Select(segment => (segment.Reason, segment.StartTime - start))).IsEquivalentTo([(SplitReason.Duration, TimeSpan.FromSeconds(61)), (SplitReason.Duration, TimeSpan.FromSeconds(122))], CollectionOrdering.Matching);
		}
	}

	// 改系统时间或校时只动墙钟
	[Test]
	[Arguments(-1)]
	[Arguments(1)]
	public async Task DurationIsMeasuredWithTheMonotonicClockAsync(int clockJumpHours, CancellationToken cancellationToken)
	{
		ShiftedWallClock time = new();
		SegmentStarts starts = new();
		Pipe pipe = new();
		await using RecordingSegmenter segmenter = new(starts.CreateWriter(pipe.Writer), new FakeSplitter(), SplitRecording.Fixed(OneMinute), time, NullLogger.Instance, new RecordingTimeline(string.Empty, time, RoomDefaults.StreamIdleTimeout));

		await segmenter.WriteAsync(FakeSplitter.Unit("a", 10), cancellationToken);
		time.Monotonic.Advance(TimeSpan.FromSeconds(30));
		time.Shift = TimeSpan.FromHours(clockJumpHours);
		await segmenter.WriteAsync(FakeSplitter.Unit("b", 10), cancellationToken);
		time.Monotonic.Advance(TimeSpan.FromSeconds(31));
		await segmenter.WriteAsync(FakeSplitter.Unit("c", 10), cancellationToken);
		await segmenter.WriteAsync(FakeSplitter.Unit("d", 10), cancellationToken);

		await Assert.That(Taken(starts).Select(static start => start.Offset)).IsEquivalentTo([20L], CollectionOrdering.Matching);
	}

	[Test]
	public async Task NothingIsSplitBeforeTheFirstSplitPointAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();

		await recording.RunAsync(new FakeSplitter(), OneMinute, static async session =>
		{
			await session.WriteAsync(FakeSplitter.Data("x", 10));
			session.Advance(TimeSpan.FromMinutes(5));
			await session.WriteAsync(FakeSplitter.Unit("a", 10));
			await session.WriteAsync(FakeSplitter.Unit("b", 10));
			await session.WriteAsync(FakeSplitter.Unit("c", 10));
		}, cancellationToken);

		// 第一个单元之前只有流头，算不上媒体数据
		await Assert.That(FakeSplitter.Describe(recording.Files)).IsEqualTo("xa | H0:bc");
	}

	[Test]
	public async Task NewFileGetsTheHeaderFromBeforeItsFirstElementAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();

		await recording.RunAsync(new FakeSplitter(), new RecordingSplit.BySize(40), static async session =>
		{
			await session.WriteAsync(FakeSplitter.Unit("a", 10));
			await session.WriteAsync(FakeSplitter.Sequence("s", 10));
			await session.WriteAsync(FakeSplitter.Unit("b", 10));
			await session.WriteAsync(FakeSplitter.Data("x", 10));
			await session.WriteAsync(FakeSplitter.Sequence("t", 10));
			await session.WriteAsync(FakeSplitter.Unit("c", 10));
		}, cancellationToken);

		// 序列头总是排在用到它的关键帧前面，属于上一个单元
		await Assert.That(FakeSplitter.Describe(recording.Files)).IsEqualTo("as | H1:bxt | H2:c");
	}

	[Test]
	public async Task ElementsSplitAcrossWritesAreReassembledAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();
		byte[] stream = [.. "abcd".SelectMany(static marker => FakeSplitter.Unit(marker.ToString(), 30))];

		await recording.RunAsync(new FakeSplitter(), new RecordingSplit.BySize(70), session => session.WriteInChunksAsync(stream, 7), cancellationToken);

		await Assert.That(FakeSplitter.Describe(recording.Files)).IsEqualTo("ab | H0:cd");
	}

	// 攒到上限还等不到切分点就先写出去，接在当前文件后面，单元剩下的部分随到随写；下一个切分点照常判断
	[Test]
	public async Task DataThatNeverReachesASplitPointIsWrittenOutWithoutStartingAFileAsync(CancellationToken cancellationToken)
	{
		FakeTimeProvider time = new();
		SegmentStarts starts = new();
		Pipe pipe = new();
		await using RecordingSegmenter segmenter = new(starts.CreateWriter(pipe.Writer), new FakeSplitter(), SplitRecording.Fixed(OneMinute), time, NullLogger.Instance, new RecordingTimeline(string.Empty, time, RoomDefaults.StreamIdleTimeout)) { MaxPendingLength = 50 };
		List<string> written = [];

		await segmenter.WriteAsync(FakeSplitter.Unit("a", 20), cancellationToken);
		time.Advance(TimeSpan.FromMinutes(2));

		foreach (char marker in "wxyz")
		{
			await segmenter.WriteAsync(FakeSplitter.Data(marker.ToString(), 20), cancellationToken);
			written.Add(Written(pipe.Reader));
		}

		await segmenter.WriteAsync(FakeSplitter.Unit("b", 20), cancellationToken);
		await segmenter.WriteAsync(FakeSplitter.Unit("c", 20), cancellationToken);
		segmenter.Complete();
		await pipe.Writer.FlushAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(written).IsEquivalentTo(["", "awx", "awxy", "awxyz"], CollectionOrdering.Matching);
			await Assert.That(Written(pipe.Reader)).IsEqualTo("awxyzH0:bc");
			await Assert.That(Taken(starts).Select(static start => (start.Offset, start.Reason))).IsEquivalentTo([(100L, SplitReason.Duration)], CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task StoppingWritesWhatWasHeldBackAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();
		using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		TaskCompletionSource written = new(TaskCreationOptions.RunContinuationsAsynchronously);

		Task<StreamEndReason> run = recording.RunAsync(new FakeSplitter(), OneMinute, async session =>
		{
			await session.WriteAsync(FakeSplitter.Unit("a", 10));
			await session.WriteAsync(FakeSplitter.Unit("b", 10));
			await session.WriteAsync([(byte)'D', 10, .. "["u8]);
			written.SetResult();
			await Task.Delay(Timeout.Infinite, session.CancellationToken);
		}, stop.Token);
		await written.Task;
		await stop.CancelAsync();

		await Assert.ThrowsAsync<OperationCanceledException>(() => run);

		// 末尾只收到 3 字节的元素解码不了，不写
		await Assert.That(FakeSplitter.Describe(recording.Files)).IsEqualTo("ab");
	}

	[Test]
	public async Task UnitTooLongToHoldStartsANewFileUnderASizeLimitAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new() { MaxPendingLength = 50 };

		// 已有的 20 字节加上攒到的 60 字节离上限还远，但单元的总长不知道
		await recording.RunAsync(new FakeSplitter(), new RecordingSplit.BySize(1000), static async session =>
		{
			await session.WriteAsync(FakeSplitter.Unit("a", 20));
			await session.WriteAsync(FakeSplitter.Unit("b", 20));

			foreach (char marker in "wxyz")
			{
				await session.WriteAsync(FakeSplitter.Data(marker.ToString(), 20));
			}

			await session.WriteAsync(FakeSplitter.Unit("c", 20));
		}, cancellationToken);

		await Assert.That(FakeSplitter.Describe(recording.Files)).IsEqualTo("a | H0:bwxyzc");
	}

	// 时长到了、标题也改了，只开着其中一种分段；收流结束时把最后半个单元切出去只会得到一个太短的文件
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task LastUnitIsNotSplitOffByDurationOrTitleChangeWhenTheStreamEndsAsync(bool byTitle, CancellationToken cancellationToken)
	{
		SplitControl control = SplitRecording.Fixed(byTitle ? new RecordingSplit.None() : OneMinute, byTitle);
		SplitRecording recording = new();

		await recording.RunAsync(new FakeSplitter(), control, async session =>
		{
			await session.WriteAsync(FakeSplitter.Unit("a", 10));
			session.Advance(TimeSpan.FromMinutes(2));
			control.RequestTitleSplit();
			await session.WriteAsync(FakeSplitter.Unit("b", 10));
			await session.WriteAsync(FakeSplitter.Data("x", 10));
		}, cancellationToken);

		await Assert.That(FakeSplitter.Describe(recording.Files)).IsEqualTo("abx");
	}

	[Test]
	public async Task LastUnitThatDoesNotFitStillStartsANewFileWhenTheStreamEndsAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();

		await recording.RunAsync(new FakeSplitter(), new RecordingSplit.BySize(50), static async session =>
		{
			await session.WriteAsync(FakeSplitter.Unit("a", 30));
			await session.WriteAsync(FakeSplitter.Unit("b", 30));
		}, cancellationToken);

		await Assert.That(FakeSplitter.Describe(recording.Files)).IsEqualTo("a | H0:b");
	}

	// 每写一次之后交给写盘端的数据：分段时攒到下一个切分点才知道单元要不要换文件；不分段时只攒分割器定时间起点要的第一个单元
	[Test]
	[Arguments(true, false, new[] { "x", "x", "xa", "xa" })]
	[Arguments(false, false, new[] { "x", "xa", "xab", "xaby" })]
	[Arguments(false, true, new[] { "x", "x", "xab", "xaby" })]
	public async Task OnlyUnitsThatMayStartANewFileOrSetTheStartAreHeldBackAsync(bool isSplitting, bool holdsFirstUnit, string[] expected, CancellationToken cancellationToken)
	{
		Pipe pipe = new();
		await using RecordingSegmenter segmenter = new(new SegmentStarts().CreateWriter(pipe.Writer), new FakeSplitter(holdsFirstUnit), SplitRecording.Fixed(isSplitting ? OneMinute : new RecordingSplit.None()), TimeProvider.System, NullLogger.Instance, new RecordingTimeline(string.Empty, TimeProvider.System, RoomDefaults.StreamIdleTimeout));
		List<string> written = [];

		foreach (byte[] element in (byte[][])[FakeSplitter.Data("x", 10), FakeSplitter.Unit("a", 10), FakeSplitter.Unit("b", 10), FakeSplitter.Data("y", 10)])
		{
			await segmenter.WriteAsync(element, cancellationToken);
			written.Add(Written(pipe.Reader));
		}

		await Assert.That(written).IsEquivalentTo(expected, CollectionOrdering.Matching);
	}

	[Test]
	public async Task TitleChangeSwitchesFilesAtTheFirstSplitPointAfterItAsync(CancellationToken cancellationToken)
	{
		SplitControl control = SplitRecording.Fixed(new RecordingSplit.None(), true);
		SplitRecording recording = new();

		await recording.RunAsync(new FakeSplitter(), control, async session =>
		{
			await session.WriteAsync(FakeSplitter.Unit("a", 10));
			await session.WriteAsync(FakeSplitter.Unit("b", 10));
			control.RequestTitleSplit();
			await session.WriteAsync(FakeSplitter.Data("x", 10));
			await session.WriteAsync(FakeSplitter.Unit("c", 10));
			await session.WriteAsync(FakeSplitter.Unit("d", 10));
		}, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(FakeSplitter.Describe(recording.Files)).IsEqualTo("abx | H0:cd");
			await Assert.That(recording.Starts.Select(static start => start.Reason)).IsEquivalentTo([SplitReason.Title], CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task TitleChangeBeforeAnyMediaOrBeforeTheRecordingIsDroppedAsync(CancellationToken cancellationToken)
	{
		SplitControl control = SplitRecording.Fixed(new RecordingSplit.None(), true);
		control.RequestTitleSplit();
		SplitRecording recording = new();

		await recording.RunAsync(new FakeSplitter(), control, async session =>
		{
			await session.WriteAsync(FakeSplitter.Data("x", 10));
			control.RequestTitleSplit();
			await session.WriteAsync(FakeSplitter.Unit("a", 10));
			await session.WriteAsync(FakeSplitter.Unit("b", 10));
			await session.WriteAsync(FakeSplitter.Unit("c", 10));
		}, cancellationToken);

		await Assert.That(FakeSplitter.Describe(recording.Files)).IsEqualTo("xabc");
	}

	[Test]
	public async Task SplitSettingChangedWhileRecordingAppliesFromTheNextSplitPointAsync(CancellationToken cancellationToken)
	{
		RecordingSplitPolicy policy = new(new RecordingSplit.None(), false);
		SplitRecording recording = new();

		await recording.RunAsync(new FakeSplitter(), new SplitControl(() => policy, static () => false), async session =>
		{
			await session.WriteAsync(FakeSplitter.Unit("a", 10));
			session.Advance(TimeSpan.FromMinutes(2));
			await session.WriteAsync(FakeSplitter.Unit("b", 10));
			policy = policy with { Split = OneMinute };
			await session.WriteAsync(FakeSplitter.Unit("c", 10));
			await session.WriteAsync(FakeSplitter.Unit("d", 10));
			session.Advance(TimeSpan.FromMinutes(2));
			policy = policy with { Split = new RecordingSplit.None() };
			await session.WriteAsync(FakeSplitter.Unit("e", 10));
			await session.WriteAsync(FakeSplitter.Unit("f", 10));
		}, cancellationToken);

		// 打开时当前文件已经超过一分钟
		await Assert.That(FakeSplitter.Describe(recording.Files)).IsEqualTo("ab | H0:cdef");
	}

	[Test]
	public async Task TitleChangedWhileTitleSplitIsOffIsNotCarriedOverOnceItIsTurnedOnAsync(CancellationToken cancellationToken)
	{
		RecordingSplitPolicy policy = new(new RecordingSplit.None(), false);
		SplitControl control = new(() => policy, static () => false);
		SplitRecording recording = new();

		await recording.RunAsync(new FakeSplitter(), control, async session =>
		{
			await session.WriteAsync(FakeSplitter.Unit("a", 10));
			await session.WriteAsync(FakeSplitter.Unit("b", 10));
			control.RequestTitleSplit();
			await session.WriteAsync(FakeSplitter.Unit("c", 10));
			await session.WriteAsync(FakeSplitter.Unit("d", 10));
			policy = policy with { OnTitleChange = true };
			await session.WriteAsync(FakeSplitter.Unit("e", 10));
			control.RequestTitleSplit();
			await session.WriteAsync(FakeSplitter.Unit("f", 10));
			await session.WriteAsync(FakeSplitter.Unit("g", 10));
		}, cancellationToken);

		await Assert.That(FakeSplitter.Describe(recording.Files)).IsEqualTo("abcde | H0:fg");
	}

	// FakeTimeProvider.AdjustTime 会连单调时钟一起挪，不像改系统时间
	private sealed class ShiftedWallClock : TimeProvider
	{
		public FakeTimeProvider Monotonic { get; } = new();

		public TimeSpan Shift { get; set; }

		public override long TimestampFrequency => Monotonic.TimestampFrequency;

		public override DateTimeOffset GetUtcNow()
		{
			return Monotonic.GetUtcNow() + Shift;
		}

		public override long GetTimestamp()
		{
			return Monotonic.GetTimestamp();
		}
	}

	// 已经交给写盘端的数据，不取走
	private static string Written(PipeReader reader)
	{
		if (!reader.TryRead(out ReadResult result))
		{
			return string.Empty;
		}

		string written = FakeSplitter.Describe(result.Buffer.ToArray());
		reader.AdvanceTo(result.Buffer.Start);
		return written;
	}

	private static List<SegmentStart> Taken(SegmentStarts starts)
	{
		List<SegmentStart> taken = [];

		while (starts.TryTake(long.MaxValue, out SegmentStart start))
		{
			taken.Add(start);
		}

		return taken;
	}
}
