using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Recording.Splitting;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class RecordingSegmenterTest
{
	private static readonly RecordingSplit OneMinute = new RecordingSplit.ByDuration(TimeSpan.FromMinutes(1));

	[Test]
	[Timeout(10_000)]
	public async Task SizeLimitSwitchesFilesBeforeAUnitThatWouldNotFitAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();

		// 头 3 字节，每个单元 30 字节，一段最多放得下头加 3 个单元
		await recording.RunAsync(new FakeSplitter(), new RecordingSplit.BySize(100), async session =>
		{
			foreach (char marker in "abcdefg")
			{
				await session.WriteAsync(FakeSplitter.Unit(marker.ToString(), 30));
			}
		}, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(Describe(recording.Files)).IsEqualTo("abc | H0:def | H0:g");
			await Assert.That(recording.Files.All(static file => file.Length <= 100)).IsTrue();
			await Assert.That(recording.Starts.Select(static start => start.Reason).Distinct().Single()).IsEqualTo(SplitReason.Size);
			await Assert.That(string.Join(", ", recording.Completed)).IsEqualTo("0, 1");
		}
	}

	[Test]
	[Timeout(10_000)]
	[Arguments(true, "abc | H0:def | H0:g")]
	[Arguments(false, "a | H0:b | H0:c | H0:d | H0:e | H0:f | H0:g")]
	public async Task SizeLimitCountsTheBytesLeftAfterRemovingFillerAsync(bool removeFiller, string expected, CancellationToken cancellationToken)
	{
		SplitRecording recording = new();

		// 每个单元 30 字节，其中一半是填充；去掉之后头 3 字节加 3 个单元正好放得下
		await recording.RunAsync(new FakeSplitter(), SplitRecording.Fixed(new RecordingSplit.BySize(50), removeFiller: removeFiller), async session =>
		{
			foreach (char marker in "abcdefg")
			{
				await session.WriteAsync(FakeSplitter.Unit(marker.ToString(), 30, 15));
			}
		}, cancellationToken);

		using (Assert.Multiple())
		{
			// 换文件的位置按写出的字节记，偏一个字节都会把元素切开
			await Assert.That(Describe(recording.Files)).IsEqualTo(expected);
			await Assert.That(recording.Files.All(static file => file.Length <= 50)).IsTrue();
			await Assert.That(recording.Files.Sum(static file => file.Count(static value => value is (byte)'F'))).IsEqualTo(removeFiller ? 0 : 7 * 15);
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task UnitLargerThanTheLimitTakesAFileOfItsOwnAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();

		await recording.RunAsync(new FakeSplitter(), new RecordingSplit.BySize(100), async session =>
		{
			await session.WriteAsync(FakeSplitter.Unit("a", 30));
			await session.WriteAsync(FakeSplitter.Unit("b", 150));
			await session.WriteAsync(FakeSplitter.Unit("c", 30));
		}, cancellationToken);

		await Assert.That(Describe(recording.Files)).IsEqualTo("a | H0:b | H0:c");
	}

	[Test]
	[Timeout(10_000)]
	public async Task DurationSwitchesFilesAtTheFirstSplitPointAfterItRunsOutAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();
		DateTimeOffset start = recording.Time.GetUtcNow();

		await recording.RunAsync(new FakeSplitter(), OneMinute, async session =>
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
			await Assert.That(Describe(recording.Files)).IsEqualTo("abxy | H0:cd | H0:ef");
			await Assert.That(recording.Starts.Select(static start => start.Reason).Distinct().Single()).IsEqualTo(SplitReason.Duration);
			await Assert.That(recording.Starts.Select(segment => segment.StartTime - start).SequenceEqual([TimeSpan.FromSeconds(61), TimeSpan.FromSeconds(122)])).IsTrue();
		}
	}

	// 改系统时间或校时只动墙钟：往前跳不会马上切出一个很短的文件，往回跳也不会推迟换文件
	[Test]
	[Timeout(10_000)]
	[Arguments(-1)]
	[Arguments(1)]
	public async Task DurationIsMeasuredWithTheMonotonicClockAsync(int clockJumpHours, CancellationToken cancellationToken)
	{
		ShiftedWallClock time = new();
		ConcurrentQueue<SegmentStart> starts = new();
		Pipe pipe = new();
		await using RecordingSegmenter segmenter = new(pipe.Writer, new FakeSplitter(), SplitRecording.Fixed(OneMinute), starts, time, NullLogger.Instance, new RecordingTimeline(string.Empty, time, RoomDefaults.StreamIdleTimeout));

		await segmenter.WriteAsync(FakeSplitter.Unit("a", 10), cancellationToken);
		time.Monotonic.Advance(TimeSpan.FromSeconds(30));
		time.Shift = TimeSpan.FromHours(clockJumpHours);
		await segmenter.WriteAsync(FakeSplitter.Unit("b", 10), cancellationToken);
		time.Monotonic.Advance(TimeSpan.FromSeconds(31));
		await segmenter.WriteAsync(FakeSplitter.Unit("c", 10), cancellationToken);
		await segmenter.WriteAsync(FakeSplitter.Unit("d", 10), cancellationToken);

		await Assert.That(starts.Select(static start => start.Offset).SequenceEqual([20L])).IsTrue();
	}

	[Test]
	[Timeout(10_000)]
	public async Task NothingIsSplitBeforeTheFirstSplitPointAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();

		await recording.RunAsync(new FakeSplitter(), OneMinute, async session =>
		{
			await session.WriteAsync(FakeSplitter.Data("x", 10));
			session.Advance(TimeSpan.FromMinutes(5));
			await session.WriteAsync(FakeSplitter.Unit("a", 10));
			await session.WriteAsync(FakeSplitter.Unit("b", 10));
			await session.WriteAsync(FakeSplitter.Unit("c", 10));
		}, cancellationToken);

		// 第一个单元之前只有流头，算不上媒体数据
		await Assert.That(Describe(recording.Files)).IsEqualTo("xa | H0:bc");
	}

	[Test]
	[Timeout(10_000)]
	public async Task NewFileGetsTheHeaderFromBeforeItsFirstElementAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();

		await recording.RunAsync(new FakeSplitter(), new RecordingSplit.BySize(40), async session =>
		{
			await session.WriteAsync(FakeSplitter.Unit("a", 10));
			await session.WriteAsync(FakeSplitter.Sequence("s", 10));
			await session.WriteAsync(FakeSplitter.Unit("b", 10));
			await session.WriteAsync(FakeSplitter.Data("x", 10));
			await session.WriteAsync(FakeSplitter.Sequence("t", 10));
			await session.WriteAsync(FakeSplitter.Unit("c", 10));
		}, cancellationToken);

		// 序列头总是排在用到它的关键帧前面，属于上一个单元
		await Assert.That(Describe(recording.Files)).IsEqualTo("as | H1:bxt | H2:c");
	}

	[Test]
	[Timeout(10_000)]
	public async Task ElementsSplitAcrossWritesAreReassembledAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();
		byte[] stream = [.. "abcd".SelectMany(static marker => FakeSplitter.Unit(marker.ToString(), 30))];

		await recording.RunAsync(new FakeSplitter(), new RecordingSplit.BySize(70), session => session.WriteInChunksAsync(stream, 7), cancellationToken);

		await Assert.That(Describe(recording.Files)).IsEqualTo("ab | H0:cd");
	}

	[Test]
	[Timeout(10_000)]
	public async Task DataThatNeverReachesASplitPointIsWrittenOutWithoutStartingAFileAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new() { MaxPendingLength = 50 };

		await recording.RunAsync(new FakeSplitter(), OneMinute, async session =>
		{
			await session.WriteAsync(FakeSplitter.Unit("a", 20));
			session.Advance(TimeSpan.FromMinutes(2));

			foreach (char marker in "wxyz")
			{
				await session.WriteAsync(FakeSplitter.Data(marker.ToString(), 20));
			}

			await session.WriteAsync(FakeSplitter.Unit("b", 20));
			await session.WriteAsync(FakeSplitter.Unit("c", 20));
		}, cancellationToken);

		// 攒不住的部分接在当前文件后面，下一个切分点照常判断
		await Assert.That(Describe(recording.Files)).IsEqualTo("awxyz | H0:bc");
	}

	[Test]
	[Timeout(10_000)]
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
		await Assert.That(Encoding.ASCII.GetString(recording.Files.Single())).EndsWith("D\n[");
	}

	[Test]
	[Timeout(10_000)]
	public async Task UnitTooLongToHoldStartsANewFileUnderASizeLimitAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new() { MaxPendingLength = 50 };

		// 已有的 20 字节加上攒到的 60 字节离上限还远，但单元的总长不知道
		await recording.RunAsync(new FakeSplitter(), new RecordingSplit.BySize(1000), async session =>
		{
			await session.WriteAsync(FakeSplitter.Unit("a", 20));
			await session.WriteAsync(FakeSplitter.Unit("b", 20));

			foreach (char marker in "wxyz")
			{
				await session.WriteAsync(FakeSplitter.Data(marker.ToString(), 20));
			}

			await session.WriteAsync(FakeSplitter.Unit("c", 20));
		}, cancellationToken);

		await Assert.That(Describe(recording.Files)).IsEqualTo("a | H0:bwxyzc");
	}

	[Test]
	[Timeout(10_000)]
	public async Task LastUnitIsNotSplitOffByDurationWhenTheStreamEndsAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();

		await recording.RunAsync(new FakeSplitter(), OneMinute, async session =>
		{
			await session.WriteAsync(FakeSplitter.Unit("a", 10));
			session.Advance(TimeSpan.FromMinutes(2));
			await session.WriteAsync(FakeSplitter.Unit("b", 10));
			await session.WriteAsync(FakeSplitter.Data("x", 10));
		}, cancellationToken);

		await Assert.That(Describe(recording.Files)).IsEqualTo("abx");
	}

	[Test]
	[Timeout(10_000)]
	public async Task LastUnitThatDoesNotFitStillStartsANewFileWhenTheStreamEndsAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();

		await recording.RunAsync(new FakeSplitter(), new RecordingSplit.BySize(50), async session =>
		{
			await session.WriteAsync(FakeSplitter.Unit("a", 30));
			await session.WriteAsync(FakeSplitter.Unit("b", 30));
		}, cancellationToken);

		await Assert.That(Describe(recording.Files)).IsEqualTo("a | H0:b");
	}

	[Test]
	[Timeout(10_000)]
	public async Task OnlyUnitsThatMayStartANewFileAreHeldBackAsync(CancellationToken cancellationToken)
	{
		Pipe pipe = new();
		await using RecordingSegmenter segmenter = new(pipe.Writer, new FakeSplitter(), SplitRecording.Fixed(OneMinute), new ConcurrentQueue<SegmentStart>(), TimeProvider.System, NullLogger.Instance, new RecordingTimeline(string.Empty, TimeProvider.System, RoomDefaults.StreamIdleTimeout));

		await segmenter.WriteAsync(FakeSplitter.Data("x", 10), cancellationToken);
		long beforeFirstSplitPoint = Buffered(pipe.Reader);
		await segmenter.WriteAsync(FakeSplitter.Unit("a", 10), cancellationToken);
		long whileUnitIsOpen = Buffered(pipe.Reader);
		await segmenter.WriteAsync(FakeSplitter.Unit("b", 10), cancellationToken);
		long afterUnitEnds = Buffered(pipe.Reader);

		await Assert.That((beforeFirstSplitPoint, whileUnitIsOpen, afterUnitEnds)).IsEqualTo((10L, 10L, 20L));
	}

	[Test]
	[Timeout(10_000)]
	public async Task WithoutSplittingNothingIsHeldBackAsync(CancellationToken cancellationToken)
	{
		Pipe pipe = new();
		await using RecordingSegmenter segmenter = new(pipe.Writer, new FakeSplitter(), SplitRecording.Fixed(new RecordingSplit.None()), new ConcurrentQueue<SegmentStart>(), TimeProvider.System, NullLogger.Instance, new RecordingTimeline(string.Empty, TimeProvider.System, RoomDefaults.StreamIdleTimeout));

		await segmenter.WriteAsync(FakeSplitter.Unit("a", 10), cancellationToken);
		await segmenter.WriteAsync(FakeSplitter.Data("x", 10), cancellationToken);

		await Assert.That(Buffered(pipe.Reader)).IsEqualTo(20L);
	}

	[Test]
	[Timeout(10_000)]
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
			await Assert.That(Describe(recording.Files)).IsEqualTo("abx | H0:cd");
			await Assert.That(recording.Starts.Single().Reason).IsEqualTo(SplitReason.Title);
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task LastUnitIsNotSplitOffByATitleChangeWhenTheStreamEndsAsync(CancellationToken cancellationToken)
	{
		SplitControl control = SplitRecording.Fixed(new RecordingSplit.None(), true);
		SplitRecording recording = new();

		await recording.RunAsync(new FakeSplitter(), control, async session =>
		{
			await session.WriteAsync(FakeSplitter.Unit("a", 10));
			control.RequestTitleSplit();
			await session.WriteAsync(FakeSplitter.Unit("b", 10));
			await session.WriteAsync(FakeSplitter.Data("x", 10));
		}, cancellationToken);

		await Assert.That(Describe(recording.Files)).IsEqualTo("abx");
	}

	[Test]
	[Timeout(10_000)]
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

		await Assert.That(Describe(recording.Files)).IsEqualTo("xabc");
	}

	[Test]
	[Timeout(10_000)]
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

		// 打开时当前文件已经超过一分钟，从打开之后的第一个切分点换；关掉之后不再换
		await Assert.That(Describe(recording.Files)).IsEqualTo("ab | H0:cdef");
	}

	[Test]
	[Timeout(10_000)]
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

		await Assert.That(Describe(recording.Files)).IsEqualTo("abcde | H0:fg");
	}

	// 去掉每个元素的类型和长度，只留下标记和新段补的头
	internal static string Describe(IEnumerable<byte[]> files)
	{
		return string.Join(" | ", files.Select(FakeSplitter.Describe));
	}

	// 只把墙钟挪开，单调时钟照常走，和改系统时间一样；FakeTimeProvider.AdjustTime 会连单调时钟一起挪
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

	private static long Buffered(PipeReader reader)
	{
		if (!reader.TryRead(out ReadResult result))
		{
			return 0;
		}

		long length = result.Buffer.Length;
		reader.AdvanceTo(result.Buffer.Start);
		return length;
	}
}

/// <summary>
/// 元素是 [类型][长度][标记][填充]：K 是切分点，S 是序列头，D 是普通数据。新段的头是 "H" 加上已经过去的序列头个数和冒号。填充里的 F 是可以去掉的填充数据。
/// </summary>
internal sealed class FakeSplitter : MediaSplitter
{
	private int _sequences;

	public static byte[] Unit(string marker, int length, int filler = 0)
	{
		byte[] unit = Element('K', marker, length);
		unit.AsSpan(length - filler).Fill((byte)'F');
		return unit;
	}

	public static byte[] Data(string marker, int length)
	{
		return Element('D', marker, length);
	}

	public static byte[] Sequence(string marker, int length)
	{
		return Element('S', marker, length);
	}

	public static string Describe(byte[] file)
	{
		StringBuilder builder = new();
		ReadOnlySpan<byte> data = file;

		while (!data.IsEmpty)
		{
			if (data[0] is (byte)'H')
			{
				int end = data.IndexOf((byte)':') + 1;
				builder.Append(Encoding.ASCII.GetString(data.Slice(0, end)));
				data = data.Slice(end);
				continue;
			}

			builder.Append((char)data[2]);
			data = data.Slice(Math.Min(data[1], data.Length));
		}

		return builder.ToString();
	}

	public override int RemoveFiller(Span<byte> elements, bool isEnabled)
	{
		int read = 0;
		int write = 0;

		while (isEnabled && elements.Length - read >= 2 && elements[read + 1] <= elements.Length - read)
		{
			int length = elements[read + 1];
			int start = write;
			elements.Slice(read, 3).CopyTo(elements.Slice(write));
			write += 3;

			foreach (byte value in elements.Slice(read + 3, length - 3))
			{
				if (value is not (byte)'F')
				{
					elements[write++] = value;
				}
			}

			elements[start + 1] = (byte)(write - start);
			read += length;
		}

		elements.Slice(read).CopyTo(elements.Slice(write));
		return write + elements.Length - read;
	}

	protected override int Measure(ReadOnlySpan<byte> data, out bool isSplit)
	{
		isSplit = data is [(byte)'K', ..];
		return data.Length < 2 ? 0 : data[1];
	}

	protected override void Accept(ReadOnlySpan<byte> element)
	{
		if (element[0] is (byte)'S')
		{
			++_sequences;
		}
	}

	protected override ReadOnlyMemory<byte> CaptureHeader(ReadOnlySpan<byte> element)
	{
		return Encoding.ASCII.GetBytes($"H{_sequences}:");
	}

	private static byte[] Element(char type, string marker, int length)
	{
		byte[] element = new byte[length];
		element.AsSpan().Fill((byte)'.');
		element[0] = (byte)type;
		element[1] = (byte)length;
		element[2] = (byte)marker[0];
		return element;
	}
}
