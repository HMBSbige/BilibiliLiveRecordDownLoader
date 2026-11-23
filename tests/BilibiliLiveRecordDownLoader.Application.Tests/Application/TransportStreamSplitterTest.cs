using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Recording.Splitting;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using System.Collections.Concurrent;
using System.IO.Pipelines;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class TransportStreamSplitterTest
{
	private static readonly RecordingSplit OneMinute = new RecordingSplit.ByDuration(TimeSpan.FromMinutes(1));

	[Test]
	public async Task NewFileStartsWithTheTablesBeforeARandomAccessFrameAsync(CancellationToken cancellationToken)
	{
		byte[] first = [.. TsSamples.Pat(), .. TsSamples.Pmt(), .. Key("k1"), .. Inter("p1")];
		byte[] repeated = [.. TsSamples.Pat(continuity: 1), .. TsSamples.Pmt(continuity: 1)];
		byte[] second = [.. Inter("p2"), .. Key("k2"), .. Inter("p3"), .. Key("k3")];
		SplitRecording recording = new();

		await recording.RunAsync(new TransportStreamSplitter(), OneMinute, async session =>
		{
			// FFmpeg 的输出按块读出，包会被切在两次写入之间
			await session.WriteInChunksAsync(first, 100);
			session.Advance(TimeSpan.FromMinutes(1));
			await session.WriteInChunksAsync([.. repeated, .. second], 100);
		}, cancellationToken);

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[[.. first, .. repeated, .. Inter("p2")], [.. TsSamples.Pat(), .. TsSamples.Pmt(), .. Key("k2"), .. Inter("p3"), .. Key("k3")]], CollectionOrdering.Matching);
	}

	[Test]
	public async Task WithoutSplittingTheFileIsTheStreamAsReceivedAsync(CancellationToken cancellationToken)
	{
		byte[] stream = [.. TsSamples.Pat(), .. TsSamples.Pmt(), .. Key("k1"), .. Inter("p1"), .. TsSamples.Pat(continuity: 1), .. TsSamples.Pmt(continuity: 1), .. Key("k2"), .. Inter("p2"), .. Key("k3")];
		SplitRecording recording = new();

		await recording.RunAsync(new TransportStreamSplitter(), new RecordingSplit.None(), session => session.WriteInChunksAsync(stream, 100), cancellationToken);

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[stream], CollectionOrdering.Matching);
	}

	[Test]
	public async Task ChangedProgramMapReplacesTheCachedOneAsync(CancellationToken cancellationToken)
	{
		byte[] changedPmt = TsSamples.Pmt(TsSamples.HevcStreamType, version: 1);
		SplitRecording recording = new();

		await recording.RunAsync(new TransportStreamSplitter(), OneMinute, async session =>
		{
			await session.WriteAsync([.. TsSamples.Pat(), .. TsSamples.Pmt(), .. Key("k1")]);
			session.Advance(TimeSpan.FromMinutes(1));
			await session.WriteAsync([.. TsSamples.Pat(), .. changedPmt, .. Key("k2"), .. Key("k3")]);
		}, cancellationToken);

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[[.. TsSamples.Pat(), .. TsSamples.Pmt(), .. Key("k1"), .. TsSamples.Pat(), .. changedPmt], [.. TsSamples.Pat(), .. changedPmt, .. Key("k2"), .. Key("k3")]], CollectionOrdering.Matching);
	}

	[Test]
	public async Task RandomAccessFrameBeforeTheTablesIsNotASplitPointAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();

		await recording.RunAsync(new TransportStreamSplitter(), OneMinute, async session =>
		{
			await session.WriteAsync(Key("k1"));
			session.Advance(TimeSpan.FromMinutes(1));
			await session.WriteAsync(Key("k2"));
			await session.WriteAsync(Key("k3"));
		}, cancellationToken);

		await Assert.That(recording.Files).Count().IsEqualTo(1);
	}

	[Test]
	public async Task LostPacketSyncStopsSplittingWithoutLosingDataAsync(CancellationToken cancellationToken)
	{
		byte[] stream = [.. TsSamples.Pat(), .. TsSamples.Pmt(), .. Key("k1"), 0x00, .. Key("k2"), .. Key("k3")];
		SplitRecording recording = new();

		await recording.RunAsync(new TransportStreamSplitter(), OneMinute, async session =>
		{
			await session.WriteAsync(stream[..^TsSamples.PacketSize]);
			session.Advance(TimeSpan.FromMinutes(2));
			await session.WriteAsync(stream[^TsSamples.PacketSize..]);
		}, cancellationToken);

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[stream], CollectionOrdering.Matching);
	}

	// 失去同步之前的关键帧照常改到 0
	[Test]
	public async Task PacketLookalikeAfterLosingSyncIsWrittenUnchangedAsync(CancellationToken cancellationToken)
	{
		byte[] lookalike = TsSamples.TimedPes(TsSamples.VideoPid, 990_000, null, 980_000);
		SplitRecording recording = new();

		await recording.RunAsync(new TransportStreamSplitter(), OneMinute, async session =>
		{
			await session.WriteAsync([.. TsSamples.Pat(), .. TsSamples.Pmt(), .. TsSamples.TimedVideoFrame(TsSamples.AvcIdr, 900_000, true)]);
			await session.WriteAsync([0x00]);
			await session.WriteAsync(lookalike);
		}, cancellationToken);

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[[.. TsSamples.Pat(), .. TsSamples.Pmt(), .. TsSamples.TimedVideoFrame(TsSamples.AvcIdr, 0, true), 0x00, .. lookalike]], CollectionOrdering.Matching);
	}

	// 停止录制时 FFmpeg 的输出可能停在一个包中间
	[Test]
	public async Task PartialPacketAtTheEndIsNotWrittenAsync(CancellationToken cancellationToken)
	{
		byte[] stream = [.. TsSamples.Pat(), .. TsSamples.Pmt(), .. Key("k1"), .. Inter("p1")];
		SplitRecording recording = new();

		await recording.RunAsync(new TransportStreamSplitter(), OneMinute, session => session.WriteAsync([.. stream, .. Key("k2").AsSpan(0, 100)]), cancellationToken);

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[stream], CollectionOrdering.Matching);
	}

	// 失去包同步之后不再知道包的边界
	[Test]
	public async Task DataAfterLosingPacketSyncIsWrittenInFullWhenTheStreamEndsAsync(CancellationToken cancellationToken)
	{
		byte[] stream = [.. TsSamples.Pat(), .. TsSamples.Pmt(), .. Key("k1"), 0x00, .. Key("k2").AsSpan(0, 100)];
		SplitRecording recording = new();

		await recording.RunAsync(new TransportStreamSplitter(), OneMinute, session => session.WriteAsync(stream), cancellationToken);

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[stream], CollectionOrdering.Matching);
	}

	[Test]
	public async Task DataAfterLosingPacketSyncIsNotHeldBackAsync(CancellationToken cancellationToken)
	{
		byte[] stream = [.. TsSamples.Pat(), .. TsSamples.Pmt(), .. Key("k1"), 0x00, .. Inter("p1")];
		Pipe pipe = new();
		await using RecordingSegmenter segmenter = new(pipe.Writer, new TransportStreamSplitter(), SplitRecording.Fixed(OneMinute), new ConcurrentQueue<SegmentStart>(), TimeProvider.System, NullLogger.Instance, new RecordingTimeline(string.Empty, TimeProvider.System, RoomDefaults.StreamIdleTimeout));

		await segmenter.WriteAsync(stream, cancellationToken);

		await Assert.That(pipe.Reader.TryRead(out ReadResult result) ? result.Buffer.Length : 0).IsEqualTo(stream.Length);
	}

	private static byte[] Key(string marker)
	{
		return [.. TsSamples.VideoFrame(TsSamples.AvcIdr, true), .. TsSamples.Audio(marker)];
	}

	private static byte[] Inter(string marker)
	{
		return [.. TsSamples.VideoFrame(TsSamples.AvcSlice), .. TsSamples.Audio(marker)];
	}
}
