using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Recording.Splitting;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
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
			await session.WriteInChunksAsync(first, 100);
			session.Advance(TimeSpan.FromMinutes(1));
			await session.WriteInChunksAsync([.. repeated, .. second], 100);
		}, cancellationToken);

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[[.. first, .. repeated, .. Inter("p2")], [.. TsSamples.Pat(), .. TsSamples.Pmt(), .. Key("k2"), .. Inter("p3"), .. Key("k3")]], CollectionOrdering.Matching);
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

		await recording.RunAsync(new TransportStreamSplitter(), OneMinute, static async session =>
		{
			await session.WriteAsync(Key("k1"));
			session.Advance(TimeSpan.FromMinutes(1));
			await session.WriteAsync(Key("k2"));
			await session.WriteAsync(Key("k3"));
		}, cancellationToken);

		await Assert.That(recording.Files).Count().IsEqualTo(1);
	}

	[Test]
	public async Task AudioOnlyProgramSplitsBeforeATimedPesAsync(CancellationToken cancellationToken)
	{
		byte[] tables = [.. TsSamples.Pat(), .. TsSamples.Pmt(hasVideo: false)];
		SplitRecording recording = new();

		await recording.RunAsync(new TransportStreamSplitter(), OneMinute, async session =>
		{
			await session.WriteAsync([.. tables, .. TsSamples.TimedPes(TsSamples.AudioPid, 90_000), .. TsSamples.TimedPes(TsSamples.AudioPid, 91_920)]);
			session.Advance(TimeSpan.FromMinutes(1));
			await session.WriteAsync(TsSamples.TimedPes(TsSamples.AudioPid, 5_490_000));
			await session.WriteAsync(TsSamples.TimedPes(TsSamples.AudioPid, 5_491_920));
		}, cancellationToken);

		byte[] rebased = [.. tables, .. TsSamples.TimedPes(TsSamples.AudioPid, 0), .. TsSamples.TimedPes(TsSamples.AudioPid, 1920)];
		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[rebased, rebased], CollectionOrdering.Matching);
	}

	[Test]
	public async Task UnrecognizedVideoStreamTypeIsNotSplitBeforeAudioAsync(CancellationToken cancellationToken)
	{
		const byte privateStreamType = 0x80;
		SplitRecording recording = new();

		await recording.RunAsync(new TransportStreamSplitter(), OneMinute, static async session =>
		{
			await session.WriteAsync([.. TsSamples.Pat(), .. TsSamples.Pmt(privateStreamType), .. TsSamples.TimedPes(TsSamples.VideoPid, 90_000), .. TsSamples.TimedPes(TsSamples.AudioPid, 90_000)]);
			session.Advance(TimeSpan.FromMinutes(1));
			await session.WriteAsync(TsSamples.TimedPes(TsSamples.AudioPid, 5_490_000));
			await session.WriteAsync(TsSamples.TimedPes(TsSamples.VideoPid, 5_490_000));
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

	[Test]
	public async Task PartialPacketAtTheEndIsNotWrittenAsync(CancellationToken cancellationToken)
	{
		byte[] stream = [.. TsSamples.Pat(), .. TsSamples.Pmt(), .. Key("k1"), .. Inter("p1")];
		SplitRecording recording = new();

		await recording.RunAsync(new TransportStreamSplitter(), OneMinute, session => session.WriteAsync([.. stream, .. Key("k2").AsSpan(0, 100)]), cancellationToken);

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[stream], CollectionOrdering.Matching);
	}

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
		await using RecordingSegmenter segmenter = new(new SegmentStarts().CreateWriter(pipe.Writer), new TransportStreamSplitter(), SplitRecording.Fixed(OneMinute), TimeProvider.System, NullLogger.Instance, new RecordingTimeline(string.Empty, TimeProvider.System, RoomDefaults.StreamIdleTimeout));

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
