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
		return new LiveStreamCandidate.Variant(codec, qn, null, [new Uri($"https://cdn.test/live-bvc/1/{codec}_{qn}/index.m3u8")]);
	}

	private static RecordingProfile Profile(long quality = 10000, bool useMasterPlaylist = true)
	{
		AppConfig config = AppConfig.CreateDefault();
		config.UseMasterPlaylistWhenRestricted = useMasterPlaylist;
		return RecordingProfile.Create(config, new RoomConfig { RoomId = 1, Quality = new StreamQuality(quality) });
	}

	private static long[] Qualities(IReadOnlyList<LiveStreamCandidate.Listed> listed, RecordingProfile profile, bool hasCookie = false)
	{
		return MasterPlaylistCandidates.GetQualities(listed, profile, hasCookie);
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
			await Assert.That(Qualities(listed, Profile(), hasCookie)).IsEquivalentTo([10000L], CollectionOrdering.Matching);
			await Assert.That(Qualities(listed, Profile(useMasterPlaylist: false), hasCookie)).IsEmpty();
		}
	}

	[Test]
	public async Task UnrestrictedPreferredStreamDoesNotAskAsync()
	{
		await Assert.That(Qualities([Listed(StreamCodec.Avc, 10000, 10000, 400, 250), Listed(StreamCodec.Hevc, 250, 10000, 400, 250)], Profile())).IsEmpty();
	}

	[Test]
	public async Task DefaultQualityNeverAsksAsync()
	{
		await Assert.That(Qualities([Listed(StreamCodec.Avc, 250, 10000, 250)], Profile(0))).IsEmpty();
	}

	[Test]
	[Arguments(false, false)]
	[Arguments(true, true)]
	public async Task OriginalTrueColorMissingFromTheApiIsAskedForAsync(bool hasCookie, bool useMasterPlaylist)
	{
		LiveStreamCandidate.Listed[] listed = [Listed(StreamCodec.Avc, 10000, 10000, 250)];

		await Assert.That(Qualities(listed, Profile(25000, useMasterPlaylist), hasCookie)).IsEquivalentTo([25000L], CollectionOrdering.Matching);
	}

	[Test]
	public async Task OriginalTrueColorFromTheApiIsNotAskedForAgainAsync()
	{
		LiveStreamCandidate.Listed[] listed = [Listed(StreamCodec.Hevc, 25000, 25000, 10000), Listed(StreamCodec.Avc, 10000, 10000)];

		await Assert.That(Qualities(listed, Profile(25000), true)).IsEmpty();
	}

	[Test]
	public async Task OriginalTrueColorFromTheApiIsTriedBeforeThePreferredCodecAsync()
	{
		LiveStreamCandidate.Listed[] listed = [Listed(StreamCodec.Avc, 10000, 10000, 250), Listed(StreamCodec.Hevc, 25000, 25000, 10000), Listed(StreamCodec.Av1, 10000, 10000)];

		using (Assert.Multiple())
		{
			await Assert.That(Describe(MasterPlaylistCandidates.PreferOriginalTrueColor(listed, Profile(25000)))).IsEqualTo("Hevc@25000 Avc@10000 Av1@10000");
			await Assert.That(Describe(MasterPlaylistCandidates.PreferOriginalTrueColor(listed, Profile()))).IsEqualTo("Avc@10000 Hevc@25000 Av1@10000");
		}
	}

	// 受限时原画真彩也可能拿不到，再要接口所列低于它的最高一档
	[Test]
	[Arguments(new long[] { 10000, 250 }, new long[] { 25000, 10000 })]
	[Arguments(new long[] { 25000, 10000, 250 }, new long[] { 25000, 10000 })]
	[Arguments(new long[] { 25000, 20000, 10000, 250 }, new long[] { 25000, 20000 })]
	[Arguments(new long[] { 25000, 250 }, new long[] { 25000 })]
	public async Task RestrictedOriginalTrueColorAlsoAsksForTheBestBelowItAsync(long[] accept, long[] expected)
	{
		LiveStreamCandidate.Listed[] listed = [Listed(StreamCodec.Avc, 250, accept)];

		using (Assert.Multiple())
		{
			await Assert.That(Qualities(listed, Profile(25000))).IsEquivalentTo(expected, CollectionOrdering.Matching);
			await Assert.That(Qualities(listed, Profile(25000, useMasterPlaylist: false))).IsEquivalentTo([25000L], CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task OriginalTrueColorIsOnlyWantedWhenSelectedAsync()
	{
		using (Assert.Multiple())
		{
			await Assert.That(Qualities([Listed(StreamCodec.Avc, 10000, 25000, 10000)], Profile())).IsEmpty();
			await Assert.That(Qualities([Listed(StreamCodec.Avc, 250, 10000, 400, 250)], Profile(400))).IsEquivalentTo([400L], CollectionOrdering.Matching);
		}
	}

	[Test]
	[Arguments(25000L, 25000L)]
	[Arguments(10000L, 10000L)]
	[Arguments(30000L, 10000L)]
	[Arguments(0L, 10000L)]
	public async Task MasterPlaylistIsRequestedAt25000OnlyForTheOriginalTrueColorAsync(long quality, long expected)
	{
		await Assert.That(MasterPlaylistCandidates.GetPlaylistQuality(Profile(quality))).IsEqualTo(expected);
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
