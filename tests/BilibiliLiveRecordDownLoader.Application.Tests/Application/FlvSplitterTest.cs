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
	[Timeout(10_000)]
	[Arguments(VideoStyle.Avc)]
	[Arguments(VideoStyle.LegacyHevc)]
	[Arguments(VideoStyle.EnhancedHevc)]
	[Arguments(VideoStyle.EnhancedAv1)]
	public async Task NewFileStartsAtAKeyFrameWithTheHeadersAndTimestampsFromZeroAsync(VideoStyle style, CancellationToken cancellationToken)
	{
		byte[] videoSequence = FlvSamples.Video(style, VideoPacket.SequenceHeader, 0, "vseq");
		byte[] first =
		[
			.. FlvSamples.Header, .. FlvSamples.Metadata(), .. videoSequence, .. FlvSamples.AudioSequenceHeader(),
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

		using (Assert.Multiple())
		{
			await Assert.That(recording.Files.Count).IsEqualTo(2);
			await Assert.That(recording.Files[0].AsSpan(0, first.Length).SequenceEqual(first)).IsTrue();
			await Assert.That(string.Join(" ", FlvSamples.Describe(recording.Files[0]))).IsEqualTo("S0 V0[vseq] A0 V0[k1] A10[a1] V33[p1] V60000[p2]");
			await Assert.That(recording.Files[1].AsSpan(0, 13).SequenceEqual(FlvSamples.Header)).IsTrue();
			await Assert.That(string.Join(" ", FlvSamples.Describe(recording.Files[1]))).IsEqualTo("S0 V0[vseq] A0 V0[k2] A0[a2] V33[p3] V2000[k3]");
		}
	}

	[Test]
	[Timeout(10_000)]
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

		await Assert.That(recording.Files.Single().SequenceEqual(stream)).IsTrue();
	}

	// 不分段时整场录制都在第一个文件里：推流重新开始时同样接着已经写出的时间排，文件里的时间和时间轴放弹幕的位置一致
	[Test]
	[Timeout(10_000)]
	public async Task TimelineThatStartsOverInTheFirstFileContinuesFromTheLastTimestampAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();

		await recording.RunAsync(new FlvSplitter(), new RecordingSplit.None(), async session =>
		{
			await session.WriteAsync([.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0, "vseq"), .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 10_000, "k1"), .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.InterFrame, 10_500, "p1")]);
			await session.WriteAsync(FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 0, "k2"));
			await session.WriteAsync(FlvSamples.Video(VideoStyle.Avc, VideoPacket.InterFrame, 500, "p2"));
		}, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(string.Join(" ", FlvSamples.Describe(recording.Files.Single()))).IsEqualTo("V0[vseq] V10000[k1] V10500[p1] V10500[k2] V11000[p2]");
			await Assert.That(recording.Timeline.GetDuration("0")).IsEqualTo(TimeSpan.FromMilliseconds(1000));
		}
	}

	// 时间轴不看序列头；中途重发的序列头时间戳是 0 时不能当成推流重新开始，否则后面的帧整体推后，文件里的时间和弹幕的位置对不上
	[Test]
	[Timeout(10_000)]
	public async Task SequenceHeaderResentWithTimestampZeroKeepsTheTimelineAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();

		await recording.RunAsync(new FlvSplitter(), new RecordingSplit.None(), async session =>
		{
			await session.WriteAsync([.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0, "vseq"), .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 10_000, "k1"), .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.InterFrame, 10_500, "p1")]);
			await session.WriteAsync([.. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0, "s2"), .. FlvSamples.AudioSequenceHeader(0, "a2")]);
			await session.WriteAsync(FlvSamples.Video(VideoStyle.Avc, VideoPacket.InterFrame, 11_000, "p2"));
		}, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(string.Join(" ", FlvSamples.Describe(recording.Files.Single()))).IsEqualTo("V0[vseq] V10000[k1] V10500[p1] V10500[s2] A10500 V11000[p2]");
			await Assert.That(recording.Timeline.GetDuration("0")).IsEqualTo(TimeSpan.FromMilliseconds(1000));
		}
	}

	[Test]
	[Timeout(10_000)]
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

		await Assert.That(string.Join(" ", FlvSamples.Describe(recording.Files[1]))).IsEqualTo("V0[vseq] V0[k2] A0[a1] V500[p1] A400[a2] V2000[k3]");
	}

	[Test]
	[Timeout(10_000)]
	public async Task TimelineThatStartsOverContinuesFromTheLastTimestampAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();

		await recording.RunAsync(new FlvSplitter(), OneMinute, async session =>
		{
			await session.WriteAsync([.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0, "vseq"), .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 0, "k1")]);
			session.Advance(TimeSpan.FromMinutes(1));
			await session.WriteAsync(FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 61_000, "k2"));
			await session.WriteAsync(FlvSamples.Video(VideoStyle.Avc, VideoPacket.InterFrame, 61_033, "p2"));

			// 重新推流：序列头和时间戳都从头开始
			await session.WriteAsync(FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0, "s2"));
			await session.WriteAsync(FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 0, "k3"));
			await session.WriteAsync(FlvSamples.Audio(10, "a3"));
			await session.WriteAsync(FlvSamples.Video(VideoStyle.Avc, VideoPacket.InterFrame, 33, "p3"));
		}, cancellationToken);

		await Assert.That(string.Join(" ", FlvSamples.Describe(recording.Files[1]))).IsEqualTo("V0[vseq] V0[k2] V33[p2] V33[s2] V33[k3] A43[a3] V66[p3]");
	}

	[Test]
	[Timeout(10_000)]
	public async Task LatestSequenceHeaderBeforeTheKeyFrameIsUsedAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();

		await recording.RunAsync(new FlvSplitter(), OneMinute, async session =>
		{
			await session.WriteAsync([.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0, "s1"), .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 0, "k1")]);
			session.Advance(TimeSpan.FromMinutes(1));

			// 分辨率变化：新的序列头紧跟在用到它的关键帧前面
			await session.WriteAsync(FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 2000, "s2"));
			await session.WriteAsync(FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 2000, "k2"));
			await session.WriteAsync(FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 4000, "k3"));
		}, cancellationToken);

		await Assert.That(string.Join(" | ", recording.Files.Select(static file => string.Join(" ", FlvSamples.Describe(file))))).IsEqualTo("V0[s1] V0[k1] V2000[s2] | V0[s2] V0[k2] V2000[k3]");
	}

	[Test]
	[Timeout(10_000)]
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

		await Assert.That(recording.Files.Count).IsEqualTo(1);
	}

	[Test]
	[Timeout(10_000)]
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

		await Assert.That(recording.Files.Single().SequenceEqual(stream)).IsTrue();
	}

	[Test]
	[Timeout(10_000)]
	public async Task DataAfterAnUnrecognizedTagInALaterFileIsWrittenUnchangedAsync(CancellationToken cancellationToken)
	{
		byte[] unrecognized = FlvSamples.Tag(0x29, 10, [1, 2, 3]);

		// 看起来像视频 tag 的头，时间戳字节是 AA BB CC DD
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
		await Assert.That(recording.Files[1].SequenceEqual(expected)).IsTrue();
	}

	[Test]
	[Timeout(10_000)]
	public async Task DataAfterAnUnrecognizedTagIsNotHeldBackAsync(CancellationToken cancellationToken)
	{
		byte[] stream = [.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0), .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 0), .. FlvSamples.Tag(0x29, 10, [1, 2, 3])];
		Pipe pipe = new();
		await using RecordingSegmenter segmenter = new(pipe.Writer, new FlvSplitter(), SplitRecording.Fixed(OneMinute), new ConcurrentQueue<SegmentStart>(), TimeProvider.System, NullLogger.Instance, new RecordingTimeline(string.Empty, TimeProvider.System, RoomDefaults.StreamIdleTimeout));

		await segmenter.WriteAsync(stream, cancellationToken);

		await Assert.That(pipe.Reader.TryRead(out ReadResult result) ? result.Buffer.Length : 0).IsEqualTo(stream.Length);
	}
}
