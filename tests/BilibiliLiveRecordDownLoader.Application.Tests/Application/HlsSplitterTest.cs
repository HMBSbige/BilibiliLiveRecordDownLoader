using BilibiliLiveRecordDownLoader.Application.Recording.Splitting;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using TUnit.Assertions.Enums;

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

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[[.. init, .. segments[0], .. segments[1]], [.. init, .. segments[2], .. segments[3], .. segments[4]]], CollectionOrdering.Matching);
	}

	[Test]
	[Timeout(10_000)]
	[Arguments(Fmp4Samples.SyncFlags, 2, DisplayName = "trex 默认同步样本")]
	[Arguments(Fmp4Samples.NonSyncFlags, 1, DisplayName = "trex 默认非同步样本")]
	public async Task Fmp4FallsBackToTheTrackExtendsDefaultFlagsAsync(uint videoTrexFlags, int files, CancellationToken cancellationToken)
	{
		byte[] init = Fmp4Samples.Init(videoTrexFlags: videoTrexFlags, tkhdVersion: 1);
		byte[] fragment = Fmp4Samples.Fragment(true, SampleFlagsSource.TrackExtendsDefault);
		SplitRecording recording = new();

		await recording.RunAsync(new Fmp4HlsSplitter(), EverySegment, session => SendAsync(session, init, [fragment, fragment, fragment]), cancellationToken);

		await Assert.That(recording.Files).Count().IsEqualTo(files);
	}

	[Test]
	[Timeout(10_000)]
	public async Task Fmp4WithoutVideoSplitsBeforeAnyFragmentAsync(CancellationToken cancellationToken)
	{
		byte[] init = Fmp4Samples.Init(hasVideo: false);
		SplitRecording recording = new();

		await recording.RunAsync(new Fmp4HlsSplitter(), EverySegment, session => SendAsync(session, init, [Fmp4Samples.Fragment(false), Fmp4Samples.Fragment(false), Fmp4Samples.Fragment(false)]), cancellationToken);

		await Assert.That(recording.Files).Count().IsEqualTo(2);
	}

	[Test]
	[Timeout(10_000)]
	public async Task TsSegmentsThatCarryTheirOwnTablesAreNotGivenMoreAsync(CancellationToken cancellationToken)
	{
		byte[][] segments = [TsSegment("1", true, TsSamples.AvcIdr), TsSegment("2", true, TsSamples.AvcSlice), TsSegment("3", true, TsSamples.AvcIdr), TsSegment("4", true, TsSamples.AvcIdr)];
		SplitRecording recording = new();

		await recording.RunAsync(new TsHlsSplitter(), EverySegment, session => SendAsync(session, null, segments), cancellationToken);

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[[.. segments[0], .. segments[1]], [.. segments[2], .. segments[3]]], CollectionOrdering.Matching);
	}

	[Test]
	[Timeout(10_000)]
	public async Task TsSegmentsWithoutTablesStartWithTheLatestOnesAsync(CancellationToken cancellationToken)
	{
		byte[][] segments = [TsSegment("1", true, TsSamples.AvcIdr), TsSegment("2", false, TsSamples.AvcIdr), TsSegment("3", false, TsSamples.AvcIdr)];
		SplitRecording recording = new();

		await recording.RunAsync(new TsHlsSplitter(), EverySegment, session => SendAsync(session, null, segments), cancellationToken);

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[segments[0], [.. TsSamples.Pat(), .. TsSamples.Pmt(), .. segments[1], .. segments[2]]], CollectionOrdering.Matching);
	}

	[Test]
	[Timeout(10_000)]
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
	[Timeout(10_000)]
	public async Task EmptySegmentDoesNotHoldBackTheFollowingOnesAsync(CancellationToken cancellationToken)
	{
		byte[] init = Fmp4Samples.Init();
		byte[][] segments = [Fmp4Samples.Fragment(true, marker: "1"), [], Fmp4Samples.Fragment(true, marker: "2"), Fmp4Samples.Fragment(true, marker: "3")];
		SplitRecording recording = new();

		await recording.RunAsync(new Fmp4HlsSplitter(), EverySegment, session => SendAsync(session, init, segments), cancellationToken);

		await Assert.That(recording.Files).IsEquivalentTo((byte[][])[[.. init, .. segments[0]], [.. init, .. segments[2], .. segments[3]]], CollectionOrdering.Matching);
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
