using BilibiliLiveRecordDownLoader.Application.Recording.Splitting;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class HlsSplitterTest
{
	private static readonly RecordingSplit EverySegment = new RecordingSplit.ByDuration(TimeSpan.FromMinutes(1));

	[Test]
	[Timeout(10_000)]
	[Arguments(SampleFlagsSource.FirstSampleFlags)]
	[Arguments(SampleFlagsSource.PerSampleFlags)]
	[Arguments(SampleFlagsSource.TrackFragmentDefault)]
	public async Task Fmp4SplitsOnlyBeforeFragmentsStartingWithASyncSampleAsync(SampleFlagsSource source, CancellationToken cancellationToken)
	{
		byte[] init = Fmp4Samples.Init();
		byte[][] segments = [Fmp4Samples.Fragment(true, source, "1"), Fmp4Samples.Fragment(false, source, "2"), Fmp4Samples.Fragment(true, source, "3"), Fmp4Samples.Fragment(false, source, "4"), Fmp4Samples.Fragment(true, source, "5")];
		SplitRecording recording = new();

		await recording.RunAsync(new Fmp4HlsSplitter(), EverySegment, session => SendAsync(session, init, segments), cancellationToken);

		await Assert.That(recording.Files.Count).IsEqualTo(2);
		await Assert.That(recording.Files[0].SequenceEqual((byte[])[.. init, .. segments[0], .. segments[1]])).IsTrue();
		await Assert.That(recording.Files[1].SequenceEqual((byte[])[.. init, .. segments[2], .. segments[3], .. segments[4]])).IsTrue();
	}

	[Test]
	[Timeout(10_000)]
	public async Task Fmp4FallsBackToTheTrackExtendsDefaultFlagsAsync(CancellationToken cancellationToken)
	{
		byte[] syncByDefault = Fmp4Samples.Init(videoTrexFlags: Fmp4Samples.SyncFlags, tkhdVersion: 1);
		byte[] nonSyncByDefault = Fmp4Samples.Init(videoTrexFlags: Fmp4Samples.NonSyncFlags, tkhdVersion: 1);
		byte[] fragment = Fmp4Samples.Fragment(true, SampleFlagsSource.TrackExtendsDefault);
		SplitRecording splits = new();
		SplitRecording doesNotSplit = new();

		await splits.RunAsync(new Fmp4HlsSplitter(), EverySegment, session => SendAsync(session, syncByDefault, [fragment, fragment, fragment]), cancellationToken);
		await doesNotSplit.RunAsync(new Fmp4HlsSplitter(), EverySegment, session => SendAsync(session, nonSyncByDefault, [fragment, fragment, fragment]), cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(splits.Files.Count).IsEqualTo(2);
			await Assert.That(doesNotSplit.Files.Count).IsEqualTo(1);
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task Fmp4WithoutVideoSplitsBeforeAnyFragmentAsync(CancellationToken cancellationToken)
	{
		byte[] init = Fmp4Samples.Init(hasVideo: false);
		SplitRecording recording = new();

		await recording.RunAsync(new Fmp4HlsSplitter(), EverySegment, session => SendAsync(session, init, [Fmp4Samples.Fragment(false), Fmp4Samples.Fragment(false), Fmp4Samples.Fragment(false)]), cancellationToken);

		await Assert.That(recording.Files.Count).IsEqualTo(2);
	}

	[Test]
	[Timeout(10_000)]
	public async Task TsSegmentsThatCarryTheirOwnTablesAreNotGivenMoreAsync(CancellationToken cancellationToken)
	{
		byte[][] segments = [TsSegment("1", true, TsSamples.AvcIdr), TsSegment("2", true, TsSamples.AvcSlice), TsSegment("3", true, TsSamples.AvcIdr), TsSegment("4", true, TsSamples.AvcIdr)];
		SplitRecording recording = new();

		await recording.RunAsync(new TsHlsSplitter(), EverySegment, session => SendAsync(session, null, segments), cancellationToken);

		await Assert.That(recording.Files.Count).IsEqualTo(2);
		await Assert.That(recording.Files[0].SequenceEqual((byte[])[.. segments[0], .. segments[1]])).IsTrue();
		await Assert.That(recording.Files[1].SequenceEqual((byte[])[.. segments[2], .. segments[3]])).IsTrue();
	}

	[Test]
	[Timeout(10_000)]
	public async Task TsSegmentsWithoutTablesStartWithTheLatestOnesAsync(CancellationToken cancellationToken)
	{
		byte[][] segments = [TsSegment("1", true, TsSamples.AvcIdr), TsSegment("2", false, TsSamples.AvcIdr), TsSegment("3", false, TsSamples.AvcIdr)];
		SplitRecording recording = new();

		await recording.RunAsync(new TsHlsSplitter(), EverySegment, session => SendAsync(session, null, segments), cancellationToken);

		await Assert.That(recording.Files.Count).IsEqualTo(2);
		await Assert.That(recording.Files[1].SequenceEqual((byte[])[.. TsSamples.Pat(), .. TsSamples.Pmt(), .. segments[1], .. segments[2]])).IsTrue();
	}

	[Test]
	[Timeout(10_000)]
	[Arguments(TsSamples.HevcStreamType, false)]
	[Arguments(TsSamples.AvcStreamType, true)]
	public async Task TsKeyFramesAreFoundByNalTypeOrRandomAccessFlagAsync(byte streamType, bool randomAccessOnly, CancellationToken cancellationToken)
	{
		(byte[] key, byte[] other) = streamType is TsSamples.HevcStreamType ? (TsSamples.HevcIdr, TsSamples.HevcTrail) : (TsSamples.AvcSlice, TsSamples.AvcSlice);
		byte[][] segments =
		[
			TsSegment("1", true, key, streamType, randomAccessOnly),
			TsSegment("2", true, other, streamType),
			TsSegment("3", true, key, streamType, randomAccessOnly),
			TsSegment("4", true, key, streamType, randomAccessOnly)
		];
		SplitRecording recording = new();

		await recording.RunAsync(new TsHlsSplitter(), EverySegment, session => SendAsync(session, null, segments), cancellationToken);

		await Assert.That(recording.Files.Select(static file => file.Length).SequenceEqual([segments[0].Length + segments[1].Length, segments[2].Length + segments[3].Length])).IsTrue();
	}

	[Test]
	[Timeout(10_000)]
	public async Task EmptySegmentDoesNotHoldBackTheFollowingOnesAsync(CancellationToken cancellationToken)
	{
		byte[] init = Fmp4Samples.Init();
		byte[][] segments = [Fmp4Samples.Fragment(true, marker: "1"), [], Fmp4Samples.Fragment(true, marker: "2"), Fmp4Samples.Fragment(true, marker: "3")];
		SplitRecording recording = new();

		await recording.RunAsync(new Fmp4HlsSplitter(), EverySegment, session => SendAsync(session, init, segments), cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(recording.Files.Count).IsEqualTo(2);
			await Assert.That(recording.Files[0].SequenceEqual((byte[])[.. init, .. segments[0]])).IsTrue();
			await Assert.That(recording.Files[1].SequenceEqual((byte[])[.. init, .. segments[2], .. segments[3]])).IsTrue();
		}
	}

	// 收流结束时最后一个单元并入最后一段
	private static async Task SendAsync(SessionWriter session, byte[]? init, byte[][] segments)
	{
		if (init is not null)
		{
			await session.InitAsync(init);
		}

		foreach (byte[] segment in segments)
		{
			await session.SegmentAsync(segment);
			session.Advance(TimeSpan.FromMinutes(1));
		}
	}

	private static byte[] TsSegment(string marker, bool withTables, byte[] picture, byte streamType = TsSamples.AvcStreamType, bool randomAccess = false)
	{
		bool isHevc = streamType is TsSamples.HevcStreamType;
		return [.. withTables ? [.. TsSamples.Pat(), .. TsSamples.Pmt(streamType)] : (byte[])[], .. TsSamples.VideoFrame(picture, randomAccess, isHevc), .. TsSamples.Audio(marker), .. TsSamples.VideoFrame(isHevc ? TsSamples.HevcTrail : TsSamples.AvcSlice, isHevc: isHevc)];
	}
}
