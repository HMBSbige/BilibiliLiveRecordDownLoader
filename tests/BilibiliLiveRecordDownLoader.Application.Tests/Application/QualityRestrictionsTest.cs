using BilibiliLive.Models;
using BilibiliLiveRecordDownLoader.Application.Recording;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class QualityRestrictionsTest
{
	private static LiveStreamCandidate.Variant Variant(StreamCodec codec, long qn)
	{
		return new LiveStreamCandidate.Variant(codec, qn, null, [new Uri($"https://cdn.test/live-bvc/1/live_a_b_c_{codec}_{qn}/index.m3u8")]);
	}

	private static LiveStreamCandidate.Listed Listed(StreamCodec codec, long qn, params long[] accept)
	{
		return new LiveStreamCandidate.Listed(StreamProtocol.HttpHls, StreamFormat.Fmp4, codec, qn, accept, [new Uri($"https://cdn.test/live-bvc/1/live_a_b_c_{codec}/index.m3u8")]);
	}

	private static RecordingProfile Profile(long quality)
	{
		return RecordingProfile.Create(AppConfig.CreateDefault(), new RoomConfig { RoomId = 1, Quality = new StreamQuality(quality) });
	}

	[Test]
	public async Task TranscodedStreamWithCookieMayHaveAnExpiredLoginAsync()
	{
		LiveStreamCandidate.Listed avc = Listed(StreamCodec.Avc, 250, 10000, 250);

		await Assert.That(QualityRestrictions.Evaluate(avc, [avc], Profile(10000), true)).IsEqualTo(new QualityRestriction.LoginMayHaveExpired(10000));
	}

	// 3990387 匿名时 AVC 和 HEVC 是超清，AV1 却是原画；AV1 排在前面，录到 AVC 说明它没打开
	[Test]
	public async Task AnotherCodecAtAHigherQualityComesBeforeTheLoginAsync()
	{
		LiveStreamCandidate.Listed avc = Listed(StreamCodec.Avc, 250, 10000, 250);
		LiveStreamCandidate.Listed hevc = Listed(StreamCodec.Hevc, 250, 10000, 250);
		LiveStreamCandidate.Listed av1Transcoded = Listed(StreamCodec.Av1, 400, 10000, 400);
		LiveStreamCandidate.Listed av1 = Listed(StreamCodec.Av1, 10000, 10000, 400);

		await Assert.That(QualityRestrictions.Evaluate(avc, [av1, av1Transcoded, avc, hevc], Profile(10000), false)).IsEqualTo(new QualityRestriction.OtherCodecUnavailable(10000, StreamCodec.Av1, 10000));
	}

	[Test]
	public async Task SameCodecInAnotherFormatIsNotAnotherCodecAsync()
	{
		LiveStreamCandidate.Listed hls = Listed(StreamCodec.Avc, 250, 10000, 250);
		LiveStreamCandidate.Listed flv = new(StreamProtocol.HttpStream, StreamFormat.Flv, StreamCodec.Avc, 10000, [10000, 250], [new Uri("https://cdn.test/live-bvc/1/live_a_b_c.flv")]);

		await Assert.That(QualityRestrictions.Evaluate(hls, [hls, flv], Profile(10000), false)).IsEqualTo(new QualityRestriction.NotLoggedIn(10000));
	}

	[Test]
	public async Task OtherCodecOffersItsOwnQualityBelowTheTargetAsync()
	{
		LiveStreamCandidate.Listed avc = Listed(StreamCodec.Avc, 250, 10000, 250);
		LiveStreamCandidate.Listed av1 = Listed(StreamCodec.Av1, 400, 10000, 400);

		await Assert.That(QualityRestrictions.Evaluate(avc, [av1, avc], Profile(10000), false)).IsEqualTo(new QualityRestriction.OtherCodecUnavailable(10000, StreamCodec.Av1, 400));
	}

	// 所选画质是蓝光时，超过它的原画排在超清之后，没有试过
	[Test]
	public async Task OtherCodecAboveTheTargetIsNotReportedAsync()
	{
		LiveStreamCandidate.Listed avc = Listed(StreamCodec.Avc, 250, 10000, 400, 250);
		LiveStreamCandidate.Listed av1 = Listed(StreamCodec.Av1, 10000, 10000, 400);

		await Assert.That(QualityRestrictions.Evaluate(avc, [avc, av1], Profile(400), false)).IsEqualTo(new QualityRestriction.NotLoggedIn(400));
	}

	[Test]
	[Arguments(400L, 250L, new long[] { 10000, 400, 250 }, 400L)]
	[Arguments(400L, 250L, new long[] { 10000, 250 }, null)]
	[Arguments(10000L, 10000L, new long[] { 25000, 10000, 250 }, null)]
	[Arguments(25000L, 10000L, new long[] { 10000, 250 }, null, DisplayName = "原画真彩不在列表里时原画就是最好的")]
	[Arguments(25000L, 10000L, new long[] { 20000, 10000, 250 }, 20000L, DisplayName = "原画真彩不在列表里时以 4K 为准")]
	[Arguments(25000L, 10000L, new long[] { 25000, 10000 }, 25000L)]
	public async Task TargetIsTheBestListedQualityNotAboveTheSelectionAsync(long quality, long qn, long[] accept, long? highest)
	{
		LiveStreamCandidate.Listed avc = Listed(StreamCodec.Avc, qn, accept);

		await Assert.That(QualityRestrictions.Evaluate(avc, [avc], Profile(quality), false)?.Highest).IsEqualTo(highest);
	}

	// 32378653：AVC 列出原画和超清，HEVC 只列超清
	[Test]
	public async Task HighestQualityComesFromTheAcceptListsOfEveryCodecAsync()
	{
		LiveStreamCandidate.Listed hevc = Listed(StreamCodec.Hevc, 250, 250);
		LiveStreamCandidate.Listed avc = Listed(StreamCodec.Avc, 250, 10000, 250);

		using (Assert.Multiple())
		{
			await Assert.That(QualityRestrictions.Evaluate(hevc, [hevc], Profile(10000), false)).IsNull();
			await Assert.That(QualityRestrictions.Evaluate(hevc, [hevc, avc], Profile(10000), false)).IsEqualTo(new QualityRestriction.NotLoggedIn(10000));
		}
	}

	[Test]
	public async Task ResponseWithoutAcceptListsIsNotJudgedAsync()
	{
		LiveStreamCandidate.Listed avc = Listed(StreamCodec.Avc, 250);

		await Assert.That(QualityRestrictions.Evaluate(avc, [avc], Profile(10000), false)).IsNull();
	}

	[Test]
	public async Task TargetJoinsTheAcceptListsOfEveryListedCodecAsync()
	{
		using (Assert.Multiple())
		{
			await Assert.That(QualityRestrictions.Target([Listed(StreamCodec.Hevc, 250, 250), Listed(StreamCodec.Avc, 250, 10000, 250)], StreamQuality.Original)).IsEqualTo(10000);
			await Assert.That(QualityRestrictions.Target([Listed(StreamCodec.Avc, 250, 400, 250), Listed(StreamCodec.Hevc, 250, 20000, 250)], StreamQuality.Dolby)).IsEqualTo(20000);
			await Assert.That(QualityRestrictions.Target([Listed(StreamCodec.Avc, 250, 10000, 250)], StreamQuality.Smooth)).IsEqualTo(250);
			await Assert.That(QualityRestrictions.Target([Variant(StreamCodec.Hevc, 25000), Listed(StreamCodec.Avc, 250)], StreamQuality.Original)).IsNull();
		}
	}

	// 哔哩哔哩默认给什么录什么
	[Test]
	public async Task DefaultQualityIsNeverRestrictedAsync()
	{
		LiveStreamCandidate.Listed avc = Listed(StreamCodec.Avc, 250, 10000, 250);

		using (Assert.Multiple())
		{
			await Assert.That(QualityRestrictions.Target([avc], StreamQuality.Default)).IsNull();
			await Assert.That(QualityRestrictions.Evaluate(avc, [avc], Profile(0), false)).IsNull();
		}
	}

	// 画质高低按 qn 比：401 排在蓝光和原画之间，表外的 qn 也一样
	[Test]
	[Arguments(new long[] { 401, 400, 250 }, 10000L, 401L)]
	[Arguments(new long[] { 401, 400, 250 }, 400L, 400L)]
	[Arguments(new long[] { 10000, 401, 400 }, 401L, 401L)]
	[Arguments(new long[] { 12345, 10000, 250 }, 15000L, 12345L)]
	[Arguments(new long[] { 12345, 10000, 250 }, 12000L, 10000L)]
	[Arguments(new long[] { 10000, 400 }, 80L, 400L, DisplayName = "都高于所选画质时取最低的一档")]
	public async Task QualitiesAreRankedByTheirQnAsync(long[] accept, long selected, long expected)
	{
		await Assert.That(QualityRestrictions.Target([Listed(StreamCodec.Avc, 250, accept)], new StreamQuality(selected))).IsEqualTo(expected);
	}
}
