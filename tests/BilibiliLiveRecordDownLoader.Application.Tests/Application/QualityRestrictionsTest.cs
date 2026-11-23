using BilibiliLive.Models;
using BilibiliLiveRecordDownLoader.Application.Recording;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class QualityRestrictionsTest
{
	private static LiveStreamCandidate.Variant Variant(StreamCodec codec, long qn)
	{
		return new LiveStreamCandidate.Variant(codec, qn, [new Uri($"https://cdn.test/live-bvc/1/live_a_b_c_{codec}_{qn}/index.m3u8")]);
	}

	private static LiveStreamCandidate.Listed Listed(StreamCodec codec, long qn, params long[] accept)
	{
		return new LiveStreamCandidate.Listed(StreamProtocol.HttpHls, StreamFormat.Fmp4, codec, qn, accept, [new Uri($"https://cdn.test/live-bvc/1/live_a_b_c_{codec}/index.m3u8")]);
	}

	private static RecordingProfile Profile(StreamQuality quality, params StreamCodec[] codecPriority)
	{
		RecordingProfile profile = RecordingProfile.Create(AppConfig.CreateDefault(), new RoomConfig { RoomId = 1, Quality = quality });
		return codecPriority.Length is 0 ? profile : profile with { CodecPriority = codecPriority };
	}

	[Test]
	public async Task TranscodedStreamWithCookieMayHaveAnExpiredLoginAsync()
	{
		LiveStreamCandidate.Listed avc = Listed(StreamCodec.Avc, 250, 10000, 250);

		await Assert.That(QualityRestrictions.Evaluate(avc, [avc], Profile(StreamQuality.Original), true)).IsEqualTo(new QualityRestriction.LoginMayHaveExpired(10000));
	}

	// 3990387 匿名时 AVC 和 HEVC 是超清，AV1 却是原画
	[Test]
	public async Task AnotherCodecAtAHigherQualityComesBeforeTheLoginAsync()
	{
		LiveStreamCandidate.Listed avc = Listed(StreamCodec.Avc, 250, 10000, 250);
		LiveStreamCandidate.Listed hevc = Listed(StreamCodec.Hevc, 250, 10000, 250);
		LiveStreamCandidate.Listed av1Transcoded = Listed(StreamCodec.Av1, 400, 10000, 400);
		LiveStreamCandidate.Listed av1 = Listed(StreamCodec.Av1, 10000, 10000, 400);

		await Assert.That(QualityRestrictions.Evaluate(avc, [avc, hevc, av1Transcoded, av1], Profile(StreamQuality.Original), false)).IsEqualTo(new QualityRestriction.OtherCodec(10000, StreamCodec.Av1, 10000));
	}

	[Test]
	public async Task SameCodecInAnotherFormatIsNotAnotherCodecAsync()
	{
		LiveStreamCandidate.Listed hls = Listed(StreamCodec.Avc, 250, 10000, 250);
		LiveStreamCandidate.Listed flv = new(StreamProtocol.HttpStream, StreamFormat.Flv, StreamCodec.Avc, 10000, [10000, 250], [new Uri("https://cdn.test/live-bvc/1/live_a_b_c.flv")]);

		await Assert.That(QualityRestrictions.Evaluate(hls, [hls, flv], Profile(StreamQuality.Original), false)).IsEqualTo(new QualityRestriction.NotLoggedIn(10000));
	}

	[Test]
	[Arguments(false)]
	public async Task OtherCodecOffersItsOwnQualityBelowTheTargetAsync(bool isOtherCodecPreferred)
	{
		LiveStreamCandidate.Listed avc = Listed(StreamCodec.Avc, 250, 10000, 250);
		LiveStreamCandidate.Listed av1 = Listed(StreamCodec.Av1, 400, 10000, 400);
		RecordingProfile profile = isOtherCodecPreferred ? Profile(StreamQuality.Original, StreamCodec.Av1, StreamCodec.Avc) : Profile(StreamQuality.Original);
		LiveStreamCandidate.Listed[] response = isOtherCodecPreferred ? [av1, avc] : [avc, av1];
		QualityRestriction expected = isOtherCodecPreferred ? new QualityRestriction.OtherCodecUnavailable(10000, StreamCodec.Av1, 400) : new QualityRestriction.OtherCodec(10000, StreamCodec.Av1, 400);

		await Assert.That(QualityRestrictions.Evaluate(avc, response, profile, false)).IsEqualTo(expected);
	}

	// 真原画拿不到时 B 站给普通原画
	[Test]
	public async Task OriginalAfterAskingForTheTrueOriginalIsNotRestrictedAsync()
	{
		RecordingProfile profile = RecordingProfile.Create(AppConfig.CreateDefault() with { RequestTrueOriginal = true }, new RoomConfig { RoomId = 1, Quality = StreamQuality.Original });
		LiveStreamCandidate.Listed avc = Listed(StreamCodec.Avc, 10000, 10000, 250);

		using (Assert.Multiple())
		{
			await Assert.That(profile.RequestQuality).IsEqualTo(StreamQualityConsts.TrueOriginal);
			await Assert.That(QualityRestrictions.Evaluate(avc, [avc], profile, false)).IsNull();
		}
	}

	[Test]
	[Arguments(StreamQuality.BluRay, 250L, new long[] { 10000, 400, 250 }, 400L)]
	[Arguments(StreamQuality.BluRay, 250L, new long[] { 10000, 250 }, null)]
	[Arguments(StreamQuality.Original, 10000L, new long[] { 25000, 10000, 250 }, null)]
	public async Task TargetIsTheBestListedQualityNotAboveTheSelectionAsync(StreamQuality quality, long qn, long[] accept, long? highest)
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
			await Assert.That(QualityRestrictions.Evaluate(hevc, [hevc], Profile(StreamQuality.Original), false)).IsNull();
			await Assert.That(QualityRestrictions.Evaluate(hevc, [hevc, avc], Profile(StreamQuality.Original), false)).IsEqualTo(new QualityRestriction.NotLoggedIn(10000));
		}
	}

	[Test]
	public async Task ResponseWithoutAcceptListsIsNotJudgedAsync()
	{
		LiveStreamCandidate.Listed avc = Listed(StreamCodec.Avc, 250);

		await Assert.That(QualityRestrictions.Evaluate(avc, [avc], Profile(StreamQuality.Original), false)).IsNull();
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

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task CodecsOutsideThePriorityAreTriedInTheOrderListedAsync(bool isOtherCodecListedFirst)
	{
		LiveStreamCandidate.Listed avc = Listed(StreamCodec.Avc, 250, 10000, 250);
		LiveStreamCandidate.Listed av1 = Listed(StreamCodec.Av1, 10000, 10000, 400);
		LiveStreamCandidate.Listed[] response = isOtherCodecListedFirst ? [av1, avc] : [avc, av1];
		QualityRestriction expected = isOtherCodecListedFirst ? new QualityRestriction.OtherCodecUnavailable(10000, StreamCodec.Av1, 10000) : new QualityRestriction.OtherCodec(10000, StreamCodec.Av1, 10000);

		await Assert.That(QualityRestrictions.Evaluate(avc, response, Profile(StreamQuality.Original, StreamCodec.Hevc), false)).IsEqualTo(expected);
	}
}
