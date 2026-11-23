using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Recording.Splitting;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using System.Collections.Concurrent;
using System.IO.Pipelines;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class FlvSplitterTest
{
	private static readonly RecordingSplit OneMinute = new RecordingSplit.ByDuration(TimeSpan.FromMinutes(1));

	[Test]
	[Arguments(VideoStyle.Avc)]
	[Arguments(VideoStyle.LegacyHevc)]
	[Arguments(VideoStyle.EnhancedHevc)]
	[Arguments(VideoStyle.EnhancedAv1)]
	public async Task NewFileStartsAtAKeyFrameWithTheHeadersAndTimestampsFromZeroAsync(VideoStyle style, CancellationToken cancellationToken)
	{
		byte[] first =
		[
			.. FlvSamples.Header, .. FlvSamples.Metadata(), .. FlvSamples.Video(style, VideoPacket.SequenceHeader, 0, "vseq"), .. FlvSamples.AudioSequenceHeader(),
			.. FlvSamples.Video(style, VideoPacket.KeyFrame, 0, "k1"), .. FlvSamples.Audio(10, "a1"), .. FlvSamples.Video(style, VideoPacket.InterFrame, 33, "p1")
		];
		SplitRecording recording = new();

		await recording.RunAsync(new FlvSplitter(), OneMinute, async session =>
		{
			await session.WriteInChunksAsync(first, 5);
			session.Advance(TimeSpan.FromMinutes(1));
			await session.WriteAsync(FlvSamples.Video(style, VideoPacket.InterFrame, 60_000, "p2"));
			await session.WriteAsync(FlvSamples.Video(style, VideoPacket.KeyFrame, 61_000, "k2"));
			await session.WriteAsync(FlvSamples.Audio(60_990, "a2"));
			await session.WriteAsync(FlvSamples.Video(style, VideoPacket.InterFrame, 61_033, "p3"));
			await session.WriteAsync(FlvSamples.Video(style, VideoPacket.KeyFrame, 63_000, "k3"));
		}, cancellationToken);

		IReadOnlyList<byte[]> files = recording.Files;
		await Assert.That(files).Count().IsEqualTo(2);

		using (Assert.Multiple())
		{
			await Assert.That(files[0].AsSpan().CommonPrefixLength(first)).IsEqualTo(first.Length);
			await Assert.That(FlvSamples.Describe(files[0])).IsEquivalentTo(["S0", "V0[vseq]", "A0", "V0[k1]", "A10[a1]", "V33[p1]", "V60000[p2]"], CollectionOrdering.Matching);
			await Assert.That(files[1].AsSpan().CommonPrefixLength(FlvSamples.Header)).IsEqualTo(FlvSamples.Header.Length);
			await Assert.That(FlvSamples.Describe(files[1])).IsEquivalentTo(["S0", "V0[vseq]", "A0", "V0[k2]", "A0[a2]", "V33[p3]", "V2000[k3]"], CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task WithoutSplittingTheFileIsTheStreamAsReceivedAsync(CancellationToken cancellationToken)
	{
		byte[] stream =
		[
			.. FlvSamples.Header, .. FlvSamples.Metadata(), .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0, "vseq"), .. FlvSamples.AudioSequenceHeader(),
			.. FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 0, "k1"), .. FlvSamples.Audio(10, "a1"), .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.InterFrame, 33, "p1"),
			.. FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 61_000, "k2"), .. FlvSamples.Audio(60_990, "a2"), .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 63_000, "k3")
		];
		SplitRecording recording = new();

		await recording.RunAsync(new FlvSplitter(), new RecordingSplit.None(), session => session.WriteInChunksAsync(stream, 7), cancellationToken);

		byte[] file = await Assert.That(recording.Files).HasSingleItem();
		await Assert.That(file).IsEquivalentTo(stream, CollectionOrdering.Matching);
	}

	// 文件里的时间要和时间轴放弹幕的位置一致
	[Test]
	public async Task TimelineThatStartsOverInTheFirstFileContinuesFromTheLastTimestampAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();

		await recording.RunAsync(new FlvSplitter(), new RecordingSplit.None(), async session =>
		{
			await session.WriteAsync([.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0, "vseq"), .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 10_000, "k1"), .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.InterFrame, 10_500, "p1")]);
			await session.WriteAsync(FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 0, "k2"));
			await session.WriteAsync(FlvSamples.Video(VideoStyle.Avc, VideoPacket.InterFrame, 500, "p2"));
		}, cancellationToken);

		byte[] file = await Assert.That(recording.Files).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(FlvSamples.Describe(file)).IsEquivalentTo(["V0[vseq]", "V0[k1]", "V500[p1]", "V500[k2]", "V1000[p2]"], CollectionOrdering.Matching);
			await Assert.That(recording.Timeline.GetDuration("0")).IsEqualTo(TimeSpan.FromMilliseconds(1000));
		}
	}

	// 时间轴不看序列头和脚本 tag，当成推流重新开始的话后面的帧整体推后，和弹幕的位置对不上
	[Test]
	public async Task SequenceHeaderResentWithTimestampZeroKeepsTheTimelineAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();

		await recording.RunAsync(new FlvSplitter(), new RecordingSplit.None(), async session =>
		{
			await session.WriteAsync([.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0, "vseq"), .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 10_000, "k1"), .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.InterFrame, 10_500, "p1")]);
			await session.WriteAsync([.. FlvSamples.Metadata(0), .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0, "s2"), .. FlvSamples.AudioSequenceHeader(0, "a2")]);
			await session.WriteAsync(FlvSamples.Video(VideoStyle.Avc, VideoPacket.InterFrame, 11_000, "p2"));
		}, cancellationToken);

		byte[] file = await Assert.That(recording.Files).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(FlvSamples.Describe(file)).IsEquivalentTo(["V0[vseq]", "V0[k1]", "V500[p1]", "S500", "V500[s2]", "A500", "V1000[p2]"], CollectionOrdering.Matching);
			await Assert.That(recording.Timeline.GetDuration("0")).IsEqualTo(TimeSpan.FromMilliseconds(1000));
		}
	}

	[Test]
	public async Task HeadersBeforeTheFirstFrameAreWrittenAtZeroAsync(CancellationToken cancellationToken)
	{
		byte[] stream =
		[
			.. FlvSamples.Header, .. FlvSamples.Metadata(8_418_000), .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 8_418_000, "vseq"), .. FlvSamples.AudioSequenceHeader(8_418_000),
			.. FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 8_418_000, "k1"), .. FlvSamples.Audio(8_418_010, "a1")
		];
		SplitRecording recording = new();

		await recording.RunAsync(new FlvSplitter(), new RecordingSplit.None(), session => session.WriteAsync(stream), cancellationToken);

		byte[] file = await Assert.That(recording.Files).HasSingleItem();
		await Assert.That(FlvSamples.Describe(file)).IsEquivalentTo(["S0", "V0[vseq]", "A0", "V0[k1]", "A10[a1]"], CollectionOrdering.Matching);
	}

	[Test]
	public async Task TagsSlightlyEarlierThanTheSegmentSoFarKeepTheTimelineAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();

		await recording.RunAsync(new FlvSplitter(), OneMinute, async session =>
		{
			await session.WriteAsync([.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0, "vseq"), .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 0, "k1")]);
			session.Advance(TimeSpan.FromMinutes(1));
			await session.WriteAsync(FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 61_000, "k2"));
			await session.WriteAsync(FlvSamples.Audio(60_800, "a1"));
			await session.WriteAsync(FlvSamples.Video(VideoStyle.Avc, VideoPacket.InterFrame, 61_500, "p1"));
			await session.WriteAsync(FlvSamples.Audio(61_400, "a2"));
			await session.WriteAsync(FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 63_000, "k3"));
		}, cancellationToken);

		await Assert.That(FlvSamples.Describe(recording.Files[1])).IsEquivalentTo(["V0[vseq]", "V0[k2]", "A0[a1]", "V500[p1]", "A400[a2]", "V2000[k3]"], CollectionOrdering.Matching);
	}

	[Test]
	public async Task LatestSequenceHeaderBeforeTheKeyFrameIsUsedAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();

		await recording.RunAsync(new FlvSplitter(), OneMinute, async session =>
		{
			await session.WriteAsync([.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0, "s1"), .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 0, "k1")]);
			session.Advance(TimeSpan.FromMinutes(1));

			await session.WriteAsync(FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 2000, "s2"));
			await session.WriteAsync(FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 2000, "k2"));
			await session.WriteAsync(FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 4000, "k3"));
		}, cancellationToken);

		await Assert.That(recording.Files.Select(static file => string.Join(" ", FlvSamples.Describe(file)))).IsEquivalentTo(["V0[s1] V0[k1] V2000[s2]", "V0[s2] V0[k2] V2000[k3]"], CollectionOrdering.Matching);
	}

	[Test]
	public async Task KeyFrameBeforeAnySequenceHeaderIsNotASplitPointAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();

		await recording.RunAsync(new FlvSplitter(), OneMinute, async session =>
		{
			await session.WriteAsync([.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 0, "k1")]);
			session.Advance(TimeSpan.FromMinutes(1));
			await session.WriteAsync(FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 2000, "k2"));
			await session.WriteAsync(FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 4000, "k3"));
		}, cancellationToken);

		await Assert.That(recording.Files).HasSingleItem();
	}

	[Test]
	public async Task UnrecognizedTagStopsSplittingWithoutLosingDataAsync(CancellationToken cancellationToken)
	{
		byte[] stream =
		[
			.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0), .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 0),
			.. FlvSamples.Tag(0x29, 10, [1, 2, 3]), .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 70_000)
		];
		SplitRecording recording = new();

		await recording.RunAsync(new FlvSplitter(), OneMinute, async session =>
		{
			await session.WriteAsync(stream.AsSpan(0, stream.Length - 30).ToArray());
			session.Advance(TimeSpan.FromMinutes(2));
			await session.WriteAsync(stream.AsSpan(stream.Length - 30).ToArray());
		}, cancellationToken);

		byte[] file = await Assert.That(recording.Files).HasSingleItem();
		await Assert.That(file).IsEquivalentTo(stream, CollectionOrdering.Matching);
	}

	// 停止录制时会话可能停在一个 tag 中间；分段时攒着的单元也一样
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task HalfTagAtTheEndIsNotWrittenAsync(bool isSplitting, CancellationToken cancellationToken)
	{
		byte[] stream = [.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0, "vseq"), .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 0, "k1"), .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.InterFrame, 33, "p1")];
		byte[] last = FlvSamples.Video(VideoStyle.Avc, VideoPacket.InterFrame, 66, "p2");
		SplitRecording recording = new();

		await recording.RunAsync(new FlvSplitter(), isSplitting ? OneMinute : new RecordingSplit.None(), session => session.WriteAsync([.. stream, .. last.AsSpan(0, last.Length - 10)]), cancellationToken);

		byte[] file = await Assert.That(recording.Files).HasSingleItem();
		await Assert.That(file).IsEquivalentTo(stream, CollectionOrdering.Matching);
	}

	// 放弃解析之后不再知道 tag 的边界，收流结束时只丢没放弃解析的末尾；签名和 tag 类型对不上就放弃，不等凑够一个头
	[Test]
	[Arguments(UnparsableEnd.HalfTagAfterAnUnrecognizedTag)]
	[Arguments(UnparsableEnd.ShortDataAfterTags)]
	[Arguments(UnparsableEnd.ShortDataInsteadOfAFileHeader)]
	public async Task DataAfterGivingUpIsWrittenInFullWhenTheStreamEndsAsync(UnparsableEnd end, CancellationToken cancellationToken)
	{
		byte[] head = [.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0), .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 0)];
		byte[] last = FlvSamples.Video(VideoStyle.Avc, VideoPacket.InterFrame, 66, "p2");
		byte[] stream = end switch
		{
			UnparsableEnd.HalfTagAfterAnUnrecognizedTag => [.. head, .. FlvSamples.Tag(0x29, 10, [1, 2, 3]), .. last.AsSpan(0, last.Length - 10)],
			UnparsableEnd.ShortDataAfterTags => [.. head, 0x29, 1, 2],
			UnparsableEnd.ShortDataInsteadOfAFileHeader => [0, 1, 2]
		};
		SplitRecording recording = new();

		await recording.RunAsync(new FlvSplitter(), OneMinute, session => session.WriteAsync(stream), cancellationToken);

		byte[] file = await Assert.That(recording.Files).HasSingleItem();
		await Assert.That(file).IsEquivalentTo(stream, CollectionOrdering.Matching);
	}

	[Test]
	public async Task DataAfterAnUnrecognizedTagInALaterFileIsWrittenUnchangedAsync(CancellationToken cancellationToken)
	{
		byte[] unrecognized = FlvSamples.Tag(0x29, 10, [1, 2, 3]);
		byte[] lookalike = [FlvSamples.VideoTag, 0, 0, 0, 0xAA, 0xBB, 0xCC, 0xDD, 0, 0, 0, 1, 2, 3, 4];
		SplitRecording recording = new();

		await recording.RunAsync(new FlvSplitter(), OneMinute, async session =>
		{
			await session.WriteAsync([.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0), .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 0)]);
			session.Advance(TimeSpan.FromMinutes(1));
			await session.WriteAsync(FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 61_000));
			await session.WriteAsync(unrecognized);
			await session.WriteAsync(lookalike);
		}, cancellationToken);

		// 放弃解析之前的关键帧照样从 0 开始
		byte[] expected = [.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0), .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 0), .. unrecognized, .. lookalike];
		await Assert.That(recording.Files[1]).IsEquivalentTo(expected, CollectionOrdering.Matching);
	}

	[Test]
	public async Task DataAfterAnUnrecognizedTagIsNotHeldBackAsync(CancellationToken cancellationToken)
	{
		byte[] stream = [.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0), .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 0), .. FlvSamples.Tag(0x29, 10, [1, 2, 3])];
		Pipe pipe = new();
		await using RecordingSegmenter segmenter = new(pipe.Writer, new FlvSplitter(), SplitRecording.Fixed(OneMinute), new ConcurrentQueue<SegmentStart>(), TimeProvider.System, NullLogger.Instance, new RecordingTimeline(string.Empty, TimeProvider.System, RoomDefaults.StreamIdleTimeout));

		await segmenter.WriteAsync(stream, cancellationToken);

		await Assert.That(pipe.Reader.TryRead(out ReadResult result) ? result.Buffer.Length : 0).IsEqualTo(stream.Length);
	}
}

public enum UnparsableEnd
{
	HalfTagAfterAnUnrecognizedTag,
	ShortDataAfterTags,
	ShortDataInsteadOfAFileHeader
}
