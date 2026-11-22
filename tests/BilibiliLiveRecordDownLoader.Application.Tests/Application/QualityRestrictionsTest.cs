using BilibiliLive.Models;
using BilibiliLiveRecordDownLoader.Application.Recording;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class QualityRestrictionsTest
{
	private static readonly LiveStreamCandidate.Source Source = new(StreamProtocol.HttpHls, StreamFormat.Fmp4, [new Uri("https://cdn.test/live-bvc/1/live_a_b_c/index.m3u8")]);

	private static LiveStreamCandidate.Listed Listed(StreamCodec codec, long qn, params long[] accept)
	{
		return new LiveStreamCandidate.Listed(StreamProtocol.HttpHls, StreamFormat.Fmp4, codec, qn, accept, [new Uri($"https://cdn.test/live-bvc/1/live_a_b_c_{codec}/index.m3u8")]);
	}

	// 默认编码优先级是 AVC、HEVC、AV1
	private static RecordingProfile Profile(StreamQuality quality, params StreamCodec[] codecPriority)
	{
		RecordingProfile profile = RecordingProfile.Create(AppConfig.CreateDefault(), new RoomConfig { RoomId = 1, Quality = quality });
		return codecPriority.Length is 0 ? profile : profile with { CodecPriority = codecPriority };
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task OriginalIsNotRestrictedAsync(bool hasCookie)
	{
		LiveStreamCandidate.Listed avc = Listed(StreamCodec.Avc, 10000, 10000, 250);

		await Assert.That(QualityRestrictions.Evaluate(avc, [avc], Profile(StreamQuality.Original), hasCookie)).IsNull();
	}

	[Test]
	public async Task TranscodedStreamWithoutCookieIsRestrictedByTheLoginAsync()
	{
		LiveStreamCandidate.Listed avc = Listed(StreamCodec.Avc, 250, 10000, 250);
		LiveStreamCandidate.Listed hevc = Listed(StreamCodec.Hevc, 250, 10000, 250);

		await Assert.That(QualityRestrictions.Evaluate(avc, [avc, hevc], Profile(StreamQuality.Original), false)).IsEqualTo(new QualityRestriction.NotLoggedIn(10000));
	}

	// 已登录的账号也可能被限，只能说登录可能失效
	[Test]
	public async Task TranscodedStreamWithCookieMayHaveAnExpiredLoginAsync()
	{
		LiveStreamCandidate.Listed avc = Listed(StreamCodec.Avc, 250, 10000, 250);

		await Assert.That(QualityRestrictions.Evaluate(avc, [avc], Profile(StreamQuality.Original), true)).IsEqualTo(new QualityRestriction.LoginMayHaveExpired(10000));
	}

	// 3990387 匿名时 AVC 和 HEVC 是超清，AV1 却是原画；编码原因优先于登录原因
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task AnotherCodecAtAHigherQualityComesBeforeTheLoginAsync(bool hasCookie)
	{
		LiveStreamCandidate.Listed avc = Listed(StreamCodec.Avc, 250, 10000, 250);
		LiveStreamCandidate.Listed hevc = Listed(StreamCodec.Hevc, 250, 10000, 250);
		LiveStreamCandidate.Listed av1Transcoded = Listed(StreamCodec.Av1, 400, 10000, 400);
		LiveStreamCandidate.Listed av1 = Listed(StreamCodec.Av1, 10000, 10000, 400);

		await Assert.That(QualityRestrictions.Evaluate(avc, [avc, hevc, av1Transcoded, av1], Profile(StreamQuality.Original), hasCookie)).IsEqualTo(new QualityRestriction.OtherCodec(10000, StreamCodec.Av1, 10000));
	}

	// 真原画拿不到时 B 站给普通原画，这是开关预期内的结果
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
	[Arguments(StreamQuality.SuperHigh, 250L, null)]
	[Arguments(StreamQuality.BluRay, 400L, null)]
	[Arguments(StreamQuality.BluRay, 250L, 400L)]
	// 选了 4K 或杜比而接口最高只列原画时，按原画算
	[Arguments(StreamQuality.Dolby, 10000L, null)]
	[Arguments(StreamQuality.Dolby, 250L, 10000L)]
	public async Task SelectedQualityBelowTheBestCapsTheTargetAsync(StreamQuality quality, long qn, long? highest)
	{
		LiveStreamCandidate.Listed avc = Listed(StreamCodec.Avc, qn, 10000, 400, 250);

		await Assert.That(QualityRestrictions.Evaluate(avc, [avc], Profile(quality), false)?.Highest).IsEqualTo(highest);
	}

	// 32378653：AVC 列出原画和超清，HEVC 只列超清；只录 HEVC 时也要按整次返回里最高的原画判断
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
	public async Task SourceStreamIsTheOriginalAsync()
	{
		LiveStreamCandidate.Listed avc = Listed(StreamCodec.Avc, 250, 10000, 250);

		await Assert.That(QualityRestrictions.Evaluate(Source, [Source, avc], Profile(StreamQuality.Original), false)).IsNull();
	}

	// 源流只是原画，选了 4K 而接口列出 4K 时仍然受限；源流的编码未知，同为 AVC 的 4K 也算更高画质的编码
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task SourceStreamBelowAHigherTargetIsRestrictedAsync(bool hasCookie)
	{
		LiveStreamCandidate.Listed avc = Listed(StreamCodec.Avc, 250, 20000, 10000, 250);
		LiveStreamCandidate.Listed av1 = Listed(StreamCodec.Av1, 20000, 20000, 10000);

		using (Assert.Multiple())
		{
			await Assert.That(QualityRestrictions.Evaluate(Source, [Source, avc], Profile(StreamQuality.UltraHd4K), hasCookie)?.Highest).IsEqualTo(20000);
			// 源流排在所有接口给的流之前，没有试过更高画质的编码
			await Assert.That(QualityRestrictions.Evaluate(Source, [Source, avc, av1], Profile(StreamQuality.UltraHd4K, StreamCodec.Av1, StreamCodec.Avc, StreamCodec.Hevc), hasCookie)).IsEqualTo(new QualityRestriction.OtherCodec(20000, StreamCodec.Av1, 20000));
			await Assert.That(QualityRestrictions.Evaluate(Source, [Source, Listed(StreamCodec.Avc, 20000, 20000, 10000)], Profile(StreamQuality.UltraHd4K), hasCookie)).IsEqualTo(new QualityRestriction.OtherCodec(20000, StreamCodec.Avc, 20000));
		}
	}

	// 所选画质不在 accept_qn 里时，按其中不高于所选的最高一档算，不能拿所选画质和最高档位取小
	[Test]
	[Arguments(StreamQuality.BluRay, 250L, new long[] { 10000, 250 }, null)]
	[Arguments(StreamQuality.BluRay, 150L, new long[] { 10000, 250, 150 }, 250L)]
	[Arguments(StreamQuality.BluRay, 250L, new long[] { 10000, 250, 150 }, null)]
	// 所选比所有档位都低时按最低一档
	[Arguments(StreamQuality.Smooth, 150L, new long[] { 10000, 250, 150 }, null)]
	[Arguments(StreamQuality.Smooth, 250L, new long[] { 10000, 250 }, null)]
	// 开了真原画，accept_qn 里有 25000 也按所选的原画算
	[Arguments(StreamQuality.Original, 10000L, new long[] { 25000, 10000, 250 }, null)]
	public async Task TargetIsTheBestListedQualityNotAboveTheSelectionAsync(StreamQuality quality, long qn, long[] accept, long? highest)
	{
		LiveStreamCandidate.Listed avc = Listed(StreamCodec.Avc, qn, accept);

		await Assert.That(QualityRestrictions.Evaluate(avc, [avc], Profile(quality), false)?.Highest).IsEqualTo(highest);
	}

	[Test]
	public async Task TargetJoinsTheAcceptListsOfEveryListedCodecAsync()
	{
		using (Assert.Multiple())
		{
			await Assert.That(QualityRestrictions.Target([Listed(StreamCodec.Hevc, 250, 250), Listed(StreamCodec.Avc, 250, 10000, 250)], StreamQuality.Original)).IsEqualTo(10000);
			await Assert.That(QualityRestrictions.Target([Listed(StreamCodec.Avc, 250, 400, 250), Listed(StreamCodec.Hevc, 250, 20000, 250)], StreamQuality.Dolby)).IsEqualTo(20000);
			await Assert.That(QualityRestrictions.Target([Listed(StreamCodec.Avc, 250, 10000, 250)], StreamQuality.Smooth)).IsEqualTo(250);
			// 源流不带 accept_qn
			await Assert.That(QualityRestrictions.Target([Source, Listed(StreamCodec.Avc, 250)], StreamQuality.Original)).IsNull();
		}
	}

	// 3990387 匿名时 AV1 是原画；AV1 排在录到的编码之后说明还没试过，排在前面说明已经试过但没连上，调整优先级也没用
	[Test]
	[Arguments(new[] { StreamCodec.Avc, StreamCodec.Hevc, StreamCodec.Av1 }, false)]
	[Arguments(new[] { StreamCodec.Av1, StreamCodec.Avc, StreamCodec.Hevc }, true)]
	[Arguments(new[] { StreamCodec.Hevc, StreamCodec.Av1, StreamCodec.Avc }, true)]
	// 不在优先级里的编码排最后
	[Arguments(new[] { StreamCodec.Avc }, false)]
	[Arguments(new[] { StreamCodec.Av1 }, true)]
	public async Task OtherCodecTriedFirstIsUnavailableAsync(StreamCodec[] codecPriority, bool unavailable)
	{
		LiveStreamCandidate.Listed avc = Listed(StreamCodec.Avc, 250, 10000, 250);
		LiveStreamCandidate.Listed av1 = Listed(StreamCodec.Av1, 10000, 10000, 400);
		QualityRestriction expected = unavailable ? new QualityRestriction.OtherCodecUnavailable(10000, StreamCodec.Av1, 10000) : new QualityRestriction.OtherCodec(10000, StreamCodec.Av1, 10000);

		await Assert.That(QualityRestrictions.Evaluate(avc, [av1, avc], Profile(StreamQuality.Original, codecPriority), false)).IsEqualTo(expected);
	}

	[Test]
	[Arguments(null, false)]
	[Arguments("", false)]
	[Arguments("  ", false)]
	[Arguments("SESSDATA=x", true)]
	public async Task OnlyAConfiguredCookieCountsAsLoggedInAsync(string? cookie, bool expected)
	{
		await Assert.That(QualityRestrictions.HasCookie(AppConfig.CreateDefault() with { Cookie = cookie })).IsEqualTo(expected);
	}
}
