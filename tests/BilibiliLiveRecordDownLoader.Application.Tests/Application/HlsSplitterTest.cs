using BilibiliLiveRecordDownLoader.Application.Recording.Splitting;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class HlsSplitterTest
{
	private static readonly RecordingSplit EverySegment = new RecordingSplit.ByDuration(TimeSpan.FromMinutes(1));

	[Test]
	[Arguments(SampleFlagsSource.FirstSampleFlags)]
	[Arguments(SampleFlagsSource.PerSampleFlags)]
	[Arguments(SampleFlagsSource.TrackFragmentDefault)]
	public async Task Fmp4SplitsOnlyBeforeFragmentsStartingWithASyncSampleAsync(SampleFlagsSource source, CancellationToken cancellationToken)
	{
		byte[] init = Fmp4Samples.Init();
		byte[][] segments = [Fmp4Samples.Fragment(true, source, "1"), Fmp4Samples.Fragment(false, source, "2"), Fmp4Samples.Fragment(true, source, "3"), Fmp4Samples.Fragment(false, source, "4"), Fmp4Samples.Fragment(true, source, "5")];
		SplitRecording recording = new();

		await recording.RunAsync(new Fmp4HlsSplitter(), EverySegment, session => SendAsync(session, init, segments), cancellationToken);

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[[.. init, .. segments[0], .. segments[1]], [.. init, .. segments[2], .. segments[3], .. segments[4]]], CollectionOrdering.Matching);
	}

	[Test]
	[Arguments(Fmp4Samples.SyncFlags, 2, DisplayName = "trex 默认同步样本")]
	[Arguments(Fmp4Samples.NonSyncFlags, 1, DisplayName = "trex 默认非同步样本")]
	[Arguments(0x0100_0000u, 1, DisplayName = "trex 默认只标了依赖别的样本")]
	public async Task Fmp4FallsBackToTheTrackExtendsDefaultFlagsAsync(uint videoTrexFlags, int files, CancellationToken cancellationToken)
	{
		byte[] init = Fmp4Samples.Init(videoTrexFlags: videoTrexFlags, tkhdVersion: 1);
		byte[] fragment = Fmp4Samples.Fragment(true, SampleFlagsSource.TrackExtendsDefault);
		SplitRecording recording = new();

		await recording.RunAsync(new Fmp4HlsSplitter(), EverySegment, session => SendAsync(session, init, [fragment, fragment, fragment]), cancellationToken);

		await Assert.That(recording.Files).Count().IsEqualTo(files);
	}

	[Test]
	public async Task Fmp4WithoutVideoSplitsBeforeAnyFragmentAsync(CancellationToken cancellationToken)
	{
		byte[] init = Fmp4Samples.Init(hasVideo: false);
		SplitRecording recording = new();

		await recording.RunAsync(new Fmp4HlsSplitter(), EverySegment, session => SendAsync(session, init, [Fmp4Samples.Fragment(false), Fmp4Samples.Fragment(false), Fmp4Samples.Fragment(false)]), cancellationToken);

		await Assert.That(recording.Files).Count().IsEqualTo(2);
	}

	[Test]
	[Arguments(false, DisplayName = "分片以 PAT 开头")]
	[Arguments(true, DisplayName = "PAT 之前有 SDT")]
	public async Task TsSegmentsThatCarryTheirOwnTablesAreNotGivenMoreAsync(bool startsWithSdt, CancellationToken cancellationToken)
	{
		byte[] sdt = startsWithSdt ? TsSamples.Packet(0x11, true, [0, 0x42]) : [];
		byte[][] segments = [[.. sdt, .. TsSegment("1", true, TsSamples.AvcIdr)], [.. sdt, .. TsSegment("2", true, TsSamples.AvcSlice)], [.. sdt, .. TsSegment("3", true, TsSamples.AvcIdr)], [.. sdt, .. TsSegment("4", true, TsSamples.AvcIdr)]];
		SplitRecording recording = new();

		await recording.RunAsync(new TsHlsSplitter(), EverySegment, session => SendAsync(session, null, segments), cancellationToken);

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[[.. segments[0], .. segments[1]], [.. segments[2], .. segments[3]]], CollectionOrdering.Matching);
	}

	[Test]
	public async Task TsSegmentsWithoutTablesStartWithTheLatestOnesAsync(CancellationToken cancellationToken)
	{
		byte[][] segments = [TsSegment("1", true, TsSamples.AvcIdr), TsSegment("2", false, TsSamples.AvcIdr), TsSegment("3", false, TsSamples.AvcIdr)];
		SplitRecording recording = new();

		await recording.RunAsync(new TsHlsSplitter(), EverySegment, session => SendAsync(session, null, segments), cancellationToken);

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[segments[0], [.. TsSamples.Pat(), .. TsSamples.Pmt(), .. segments[1], .. segments[2]]], CollectionOrdering.Matching);
	}

	[Test]
	public async Task ChangedTsProgramMapReplacesTheCachedOneAsync(CancellationToken cancellationToken)
	{
		byte[] changedPmt = TsSamples.Pmt(version: 1);
		byte[][] segments = [TsSegment("1", true, TsSamples.AvcIdr), [.. TsSamples.Pat(), .. changedPmt, .. TsSegment("2", false, TsSamples.AvcSlice)], TsSegment("3", false, TsSamples.AvcIdr), TsSegment("4", false, TsSamples.AvcIdr)];
		SplitRecording recording = new();

		await recording.RunAsync(new TsHlsSplitter(), EverySegment, session => SendAsync(session, null, segments), cancellationToken);

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[[.. segments[0], .. segments[1]], [.. TsSamples.Pat(), .. changedPmt, .. segments[2], .. segments[3]]], CollectionOrdering.Matching);
	}

	[Test]
	public async Task TsSegmentsBeforeTheTablesAreNotSplitPointsAsync(CancellationToken cancellationToken)
	{
		byte[][] segments = [TsSegment("1", false, TsSamples.AvcIdr), TsSegment("2", false, TsSamples.AvcIdr), TsSegment("3", false, TsSamples.AvcIdr)];
		SplitRecording recording = new();

		await recording.RunAsync(new TsHlsSplitter(), EverySegment, session => SendAsync(session, null, segments), cancellationToken);

		await Assert.That(recording.Files).Count().IsEqualTo(1);
	}

	[Test]
	public async Task AudioOnlyTsSegmentsAreAllSplitPointsAsync(CancellationToken cancellationToken)
	{
		byte[] tables = [.. TsSamples.Pat(), .. TsSamples.Pmt(hasVideo: false)];
		byte[][] segments = [[.. tables, .. TsSamples.Audio("1")], TsSamples.Audio("2"), TsSamples.Audio("3"), TsSamples.Audio("4")];
		SplitRecording recording = new();

		await recording.RunAsync(new TsHlsSplitter(), EverySegment, session => SendAsync(session, null, segments), cancellationToken);

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[segments[0], [.. tables, .. segments[1]], [.. tables, .. segments[2], .. segments[3]]], CollectionOrdering.Matching);
	}

	[Test]
	[Arguments(TsSamples.HevcStreamType, false, DisplayName = "HEVC 按 NAL 类型")]
	[Arguments(TsSamples.AvcStreamType, true, DisplayName = "AVC 按随机访问标志")]
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

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[[.. segments[0], .. segments[1]], [.. segments[2], .. segments[3]]], CollectionOrdering.Matching);
	}

	[Test]
	public async Task EmptySegmentDoesNotHoldBackTheFollowingOnesAsync(CancellationToken cancellationToken)
	{
		byte[] init = Fmp4Samples.Init();
		byte[][] segments = [Fmp4Samples.Fragment(true, marker: "1"), [], Fmp4Samples.Fragment(true, marker: "2"), Fmp4Samples.Fragment(true, marker: "3")];
		SplitRecording recording = new();

		await recording.RunAsync(new Fmp4HlsSplitter(), EverySegment, session => SendAsync(session, init, segments), cancellationToken);

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[[.. init, .. segments[0]], [.. init, .. segments[2], .. segments[3]]], CollectionOrdering.Matching);
	}

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
