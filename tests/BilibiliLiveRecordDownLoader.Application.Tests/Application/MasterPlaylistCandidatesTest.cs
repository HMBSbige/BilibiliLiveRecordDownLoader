using BilibiliLive.Models;
using BilibiliLiveRecordDownLoader.Application.Recording;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class MasterPlaylistCandidatesTest
{
	private static LiveStreamCandidate.Listed Listed(StreamCodec codec, long qn, params long[] accept)
	{
		return new LiveStreamCandidate.Listed(StreamProtocol.HttpHls, StreamFormat.Fmp4, codec, qn, accept, [new Uri($"https://cdn.test/live-bvc/1/live_a_b_c_{codec}/index.m3u8")]);
	}

	private static LiveStreamCandidate.Variant Variant(StreamCodec codec, long qn)
	{
		return new LiveStreamCandidate.Variant(codec, qn, [new Uri($"https://cdn.test/live-bvc/1/{codec}_{qn}/index.m3u8")]);
	}

	// 默认编码优先级是 AVC、HEVC、AV1
	private static RecordingProfile Profile(StreamQuality quality = StreamQuality.Original, bool useMasterPlaylist = true, bool requestTrueOriginal = false)
	{
		AppConfig config = AppConfig.CreateDefault();
		config.UseMasterPlaylistWhenRestricted = useMasterPlaylist;
		config.RequestTrueOriginal = requestTrueOriginal;
		return RecordingProfile.Create(config, new RoomConfig { RoomId = 1, Quality = quality });
	}

	private static string Qualities(IReadOnlyList<LiveStreamCandidate.Listed> listed, RecordingProfile profile, bool hasCookie = false)
	{
		return string.Join(' ', MasterPlaylistCandidates.GetQualities(listed, profile, hasCookie));
	}

	private static string Describe(IEnumerable<LiveStreamCandidate> candidates)
	{
		return string.Join(' ', candidates.Select(static candidate => $"{candidate.Codec}@{candidate.Qn}"));
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task RestrictedPreferredStreamAsksForTheTargetAsync(bool hasCookie)
	{
		LiveStreamCandidate.Listed[] listed = [Listed(StreamCodec.Avc, 250, 10000, 250), Listed(StreamCodec.Hevc, 250, 10000, 250)];

		using (Assert.Multiple())
		{
			await Assert.That(Qualities(listed, Profile(), hasCookie)).IsEqualTo("10000");
			await Assert.That(Qualities(listed, Profile(useMasterPlaylist: false), hasCookie)).IsEmpty();
		}
	}

	// 目标档位是不高于所选画质的最高一档
	[Test]
	[Arguments(StreamQuality.BluRay, new long[] { 10000, 400, 250 }, "400")]
	[Arguments(StreamQuality.BluRay, new long[] { 10000, 250, 150 }, "250")]
	[Arguments(StreamQuality.UltraHd4K, new long[] { 20000, 10000, 250 }, "20000")]
	public async Task TargetFollowsTheSelectedQualityAsync(StreamQuality quality, long[] accept, string expected)
	{
		LiveStreamCandidate.Listed[] listed = [Listed(StreamCodec.Avc, 150, accept)];

		await Assert.That(Qualities(listed, Profile(quality))).IsEqualTo(expected);
	}

	[Test]
	[Arguments(StreamQuality.Original, 10000L)]
	[Arguments(StreamQuality.BluRay, 400L)]
	public async Task UnrestrictedPreferredStreamDoesNotAskAsync(StreamQuality quality, long qn)
	{
		await Assert.That(Qualities([Listed(StreamCodec.Avc, qn, 10000, 400, 250), Listed(StreamCodec.Hevc, 250, 10000, 400, 250)], Profile(quality))).IsEmpty();
	}

	[Test]
	[Arguments(false, false)]
	[Arguments(true, false)]
	[Arguments(true, true)]
	public async Task TrueOriginalMissingFromTheApiIsAskedForAsync(bool hasCookie, bool useMasterPlaylist)
	{
		LiveStreamCandidate.Listed[] listed = [Listed(StreamCodec.Avc, 10000, 10000, 250)];

		await Assert.That(Qualities(listed, Profile(useMasterPlaylist: useMasterPlaylist, requestTrueOriginal: true), hasCookie)).IsEqualTo("25000");
	}

	[Test]
	public async Task TrueOriginalFromTheApiIsNotAskedForAgainAsync()
	{
		LiveStreamCandidate.Listed[] listed = [Listed(StreamCodec.Hevc, 25000, 25000, 10000), Listed(StreamCodec.Avc, 10000, 10000)];

		await Assert.That(Qualities(listed, Profile(requestTrueOriginal: true), true)).IsEmpty();
	}

	// 接口可能只在一个编码上给真原画
	[Test]
	public async Task TrueOriginalFromTheApiIsTriedBeforeThePreferredCodecAsync()
	{
		LiveStreamCandidate.Listed[] listed = [Listed(StreamCodec.Avc, 10000, 10000, 250), Listed(StreamCodec.Hevc, 25000, 25000, 10000), Listed(StreamCodec.Av1, 10000, 10000)];

		using (Assert.Multiple())
		{
			await Assert.That(Describe(MasterPlaylistCandidates.PreferTrueOriginal(listed, Profile(requestTrueOriginal: true)))).IsEqualTo("Hevc@25000 Avc@10000 Av1@10000");
			await Assert.That(Describe(MasterPlaylistCandidates.PreferTrueOriginal(listed, Profile()))).IsEqualTo("Avc@10000 Hevc@25000 Av1@10000");
		}
	}

	[Test]
	public async Task RestrictedTrueOriginalAsksForBothAsync()
	{
		LiveStreamCandidate.Listed[] listed = [Listed(StreamCodec.Avc, 250, 10000, 250)];

		using (Assert.Multiple())
		{
			await Assert.That(Qualities(listed, Profile(requestTrueOriginal: true))).IsEqualTo("25000 10000");
			await Assert.That(Qualities(listed, Profile(useMasterPlaylist: false, requestTrueOriginal: true))).IsEqualTo("25000");
		}
	}

	[Test]
	public async Task TrueOriginalIsOnlyWantedAtTheOriginalAsync()
	{
		using (Assert.Multiple())
		{
			await Assert.That(Qualities([Listed(StreamCodec.Avc, 400, 10000, 400)], Profile(StreamQuality.BluRay, requestTrueOriginal: true))).IsEmpty();
			await Assert.That(Qualities([Listed(StreamCodec.Avc, 250, 10000, 400, 250)], Profile(StreamQuality.BluRay, requestTrueOriginal: true))).IsEqualTo("400");
		}
	}

	[Test]
	[Arguments(StreamQuality.Original, true, 42L)]
	[Arguments(StreamQuality.Original, false, 0L)]
	[Arguments(StreamQuality.BluRay, true, 0L)]
	public async Task MidIsTheStreamerOnlyForTheTrueOriginalAsync(StreamQuality quality, bool requestTrueOriginal, long expected)
	{
		await Assert.That(MasterPlaylistCandidates.GetMid(Profile(quality, requestTrueOriginal: requestTrueOriginal), 42)).IsEqualTo(expected);
	}

	[Test]
	public async Task VariantsAreOrderedByQualityThenCodecPriorityAsync()
	{
		LiveStreamCandidate.Variant[] variants = [Variant(StreamCodec.Hevc, 10000), Variant(StreamCodec.Avc, 10000), Variant(StreamCodec.Hevc, 400), Variant(StreamCodec.Avc, 400), Variant(StreamCodec.Hevc, 25000)];

		IReadOnlyList<LiveStreamCandidate.Variant> selected = MasterPlaylistCandidates.Select(variants, [25000, 10000], [StreamCodec.Avc, StreamCodec.Hevc, StreamCodec.Av1]);
		IReadOnlyList<LiveStreamCandidate.Variant> hevcFirst = MasterPlaylistCandidates.Select(variants, [10000], [StreamCodec.Hevc, StreamCodec.Avc, StreamCodec.Av1]);

		using (Assert.Multiple())
		{
			await Assert.That(Describe(selected)).IsEqualTo("Hevc@25000 Avc@10000 Hevc@10000");
			await Assert.That(Describe(hevcFirst)).IsEqualTo("Hevc@10000 Avc@10000");
		}
	}
}
