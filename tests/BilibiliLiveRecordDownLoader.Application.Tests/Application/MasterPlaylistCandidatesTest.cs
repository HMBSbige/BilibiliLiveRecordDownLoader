using BilibiliLive.Models;
using BilibiliLiveRecordDownLoader.Application.Recording;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class MasterPlaylistCandidatesTest
{
	private static LiveStreamCandidate.Variant Variant(StreamCodec codec, long qn)
	{
		return new LiveStreamCandidate.Variant(codec, qn, null, [new Uri($"https://cdn.test/live-bvc/1/{codec}_{qn}/index.m3u8")]);
	}

	private static LiveStreamCandidate.Listed Listed(StreamCodec codec, long qn)
	{
		return new LiveStreamCandidate.Listed(StreamProtocol.HttpHls, StreamFormat.Fmp4, codec, qn, [], [new Uri($"https://cdn.test/live-bvc/1/live_a_b_c_{codec}/index.m3u8")]);
	}

	private static RecordingProfile Profile(long quality, params StreamCodec[] codecPriority)
	{
		return RecordingProfile.Create(AppConfig.CreateDefault(), new RoomConfig { RoomId = 1, Quality = new StreamQuality(quality), CodecPriority = codecPriority });
	}

	private static string Batch(LiveStreamCandidate.Listed[] listed, LiveStreamCandidate.Variant[] variants, RecordingProfile profile)
	{
		IReadOnlyList<LiveStreamCandidate> batch = CandidateOrder.Sort<LiveStreamCandidate>([.. listed, .. MasterPlaylistCandidates.Select(variants, listed, profile)], profile);
		return string.Join(' ', batch.Select(static candidate => $"{(candidate is LiveStreamCandidate.Listed ? "api:" : "")}{candidate.Codec}@{candidate.Qn}"));
	}

	// 高于原画的档位只在用对应的 qn 请求时才列出
	[Test]
	[Arguments(20000L, 25000L, "20000", 10000L)]
	[Arguments(25000L, 30000L, "25000", 10000L, DisplayName = "原画真彩及以下只请求一次")]
	[Arguments(30000L, 30000L, "30000", 30000L)]
	[Arguments(30000L, 25000L, "30000 25000", 25000L, DisplayName = "没有杜比时改用原画真彩的 qn 请求")]
	public async Task MasterPlaylistIsRequestedAtTheSelectedQualityAsync(long quality, long roomBest, string requested, long best)
	{
		List<long> requests = [];

		IReadOnlyList<LiveStreamCandidate.Variant> variants = await MasterPlaylistCandidates.FetchAsync((qn, _) =>
		{
			requests.Add(qn);
			return Task.FromResult<IReadOnlyList<LiveStreamCandidate.Variant>>(qn == roomBest ? [Variant(StreamCodec.Hevc, qn), Variant(StreamCodec.Avc, 10000)] : [Variant(StreamCodec.Avc, 10000)]);
		}, new StreamQuality(quality), CancellationToken.None);

		using (Assert.Multiple())
		{
			await Assert.That(string.Join(' ', requests)).IsEqualTo(requested);
			await Assert.That(variants.Max(static variant => variant.Qn)).IsEqualTo(best);
		}
	}

	// 请求失败时 fetch 返回空列表
	[Test]
	[Arguments(new long[0], "30000", DisplayName = "第一次就失败时不再请求")]
	[Arguments(new[] { 10000L, 400L }, "30000 25000", DisplayName = "第二次失败时沿用第一次的变体")]
	public async Task FetchStopsAtAFailedRequestAsync(long[] first, string requested)
	{
		List<long> requests = [];

		IReadOnlyList<LiveStreamCandidate.Variant> variants = await MasterPlaylistCandidates.FetchAsync((qn, _) =>
		{
			requests.Add(qn);
			return Task.FromResult<IReadOnlyList<LiveStreamCandidate.Variant>>(qn == StreamQuality.Dolby.Qn ? [.. first.Select(static value => Variant(StreamCodec.Avc, value))] : []);
		}, StreamQuality.Dolby, CancellationToken.None);

		using (Assert.Multiple())
		{
			await Assert.That(string.Join(' ', requests)).IsEqualTo(requested);
			await Assert.That(variants.Select(static variant => variant.Qn)).IsEquivalentTo(first, CollectionOrdering.Matching);
		}
	}

	[Test]
	[Arguments(25000L, new StreamCodec[0], "Hevc@25000 Avc@10000 Hevc@10000 Avc@400 Hevc@400 api:Avc@250 api:Hevc@250", DisplayName = "画质高的排前面，同一画质按编码优先级")]
	[Arguments(10000L, new[] { StreamCodec.Hevc, StreamCodec.Avc, StreamCodec.Av1 }, "Hevc@10000 Avc@10000 Hevc@400 Avc@400 api:Hevc@250 api:Avc@250", DisplayName = "不要高于所选画质的档位")]
	public async Task VariantsUpToTheSelectionComeBeforeLowerListedStreamsAsync(long quality, StreamCodec[] codecPriority, string expected)
	{
		LiveStreamCandidate.Listed[] listed = [Listed(StreamCodec.Avc, 250), Listed(StreamCodec.Hevc, 250)];
		LiveStreamCandidate.Variant[] variants = [Variant(StreamCodec.Hevc, 10000), Variant(StreamCodec.Avc, 10000), Variant(StreamCodec.Hevc, 400), Variant(StreamCodec.Avc, 400), Variant(StreamCodec.Hevc, 25000), Variant(StreamCodec.Avc, 250)];

		await Assert.That(Batch(listed, variants, Profile(quality, codecPriority))).IsEqualTo(expected);
	}

	[Test]
	[Arguments(25000L, "Hevc@25000 Avc@10000 Hevc@10000 api:Av1@10000 Avc@400 api:Avc@250 api:Hevc@250")]
	[Arguments(10000L, "Avc@10000 Hevc@10000 api:Av1@10000 Avc@400 api:Avc@250 api:Hevc@250", DisplayName = "接口已有所选画质")]
	public async Task VariantsFillTheQualitiesSomeListedCodecsLackAsync(long quality, string expected)
	{
		LiveStreamCandidate.Listed[] listed = [Listed(StreamCodec.Av1, 10000), Listed(StreamCodec.Avc, 250), Listed(StreamCodec.Hevc, 250)];
		LiveStreamCandidate.Variant[] variants = [Variant(StreamCodec.Hevc, 25000), Variant(StreamCodec.Av1, 10000), Variant(StreamCodec.Avc, 10000), Variant(StreamCodec.Hevc, 10000), Variant(StreamCodec.Avc, 400), Variant(StreamCodec.Avc, 250), Variant(StreamCodec.Avc, 150)];

		await Assert.That(Batch(listed, variants, Profile(quality))).IsEqualTo(expected);
	}
}
