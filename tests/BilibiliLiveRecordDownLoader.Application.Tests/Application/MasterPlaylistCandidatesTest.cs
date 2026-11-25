using BilibiliLive.Models;
using BilibiliLiveRecordDownLoader.Application.Recording;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class MasterPlaylistCandidatesTest
{
	private static LiveStreamCandidate.Variant Variant(StreamCodec codec, long qn)
	{
		return new LiveStreamCandidate.Variant(codec, qn, null, [new Uri($"https://cdn.test/live-bvc/1/{codec}_{qn}/index.m3u8")]);
	}

	private static RecordingProfile Profile(long quality, params StreamCodec[] codecPriority)
	{
		RecordingProfile profile = RecordingProfile.Create(AppConfig.CreateDefault(), new RoomConfig { RoomId = 1, Quality = new StreamQuality(quality) });
		return codecPriority.Length is 0 ? profile : profile with { CodecPriority = codecPriority };
	}

	private static string Describe(IEnumerable<LiveStreamCandidate> candidates)
	{
		return string.Join(' ', candidates.Select(static candidate => $"{candidate.Codec}@{candidate.Qn}"));
	}

	[Test]
	[Arguments(30000L, 30000L)]
	[Arguments(25000L, 25000L)]
	[Arguments(20000L, 20000L)]
	[Arguments(10000L, 10000L)]
	[Arguments(401L, 401L)]
	[Arguments(250L, 250L)]
	[Arguments(0L, 0L)]
	public async Task MasterPlaylistIsRequestedAtTheSelectedQualityAsync(long quality, long expected)
	{
		await Assert.That(MasterPlaylistCandidates.GetPlaylistQuality(Profile(quality))).IsEqualTo(expected);
	}

	[Test]
	[Arguments(25000L, 400L, new StreamCodec[0], "Hevc@25000 Avc@10000 Hevc@10000", DisplayName = "画质高的排前面，同一画质按编码优先级")]
	[Arguments(10000L, 250L, new StreamCodec[0], "Avc@10000 Hevc@10000 Avc@400 Hevc@400", DisplayName = "低于所选画质的档位也要")]
	[Arguments(10000L, 400L, new[] { StreamCodec.Hevc, StreamCodec.Avc, StreamCodec.Av1 }, "Hevc@10000 Avc@10000", DisplayName = "不要不高于接口最好那路的档位")]
	[Arguments(20000L, 10000L, new StreamCodec[0], "", DisplayName = "不要高于所选画质的档位")]
	public async Task VariantsAboveTheListedBestUpToTheSelectionAreOrderedByQualityThenCodecAsync(long quality, long listedBest, StreamCodec[] codecPriority, string expected)
	{
		LiveStreamCandidate.Variant[] variants = [Variant(StreamCodec.Hevc, 10000), Variant(StreamCodec.Avc, 10000), Variant(StreamCodec.Hevc, 400), Variant(StreamCodec.Avc, 400), Variant(StreamCodec.Hevc, 25000), Variant(StreamCodec.Avc, 250)];

		await Assert.That(Describe(MasterPlaylistCandidates.Select(variants, listedBest, Profile(quality, codecPriority)))).IsEqualTo(expected);
	}
}
