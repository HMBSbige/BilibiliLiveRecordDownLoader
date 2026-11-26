using BilibiliLive.Models;
using BilibiliLiveRecordDownLoader.Application.Recording;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class CandidateOrderTest
{
	private static LiveStreamCandidate.Listed Listed(StreamCodec codec, long qn, StreamFormat format = StreamFormat.Fmp4, string? streamName = null)
	{
		StreamProtocol protocol = format is StreamFormat.Flv ? StreamProtocol.HttpStream : StreamProtocol.HttpHls;
		return new LiveStreamCandidate.Listed(protocol, format, codec, qn, [], [new Uri($"https://cdn.test/live-bvc/1/live_a_b_c_{codec}_{format}/index.m3u8")]) { StreamName = streamName };
	}

	private static LiveStreamCandidate.Variant Variant(StreamCodec codec, long qn, string streamName)
	{
		return new LiveStreamCandidate.Variant(codec, qn, null, [new Uri($"https://cdn.test/live-bvc/1/{streamName}/index.m3u8")]) { StreamName = streamName };
	}

	private static RecordingProfile Profile(long quality, StreamCodec[]? codecPriority = null, StreamFormat[]? formatPriority = null)
	{
		return RecordingProfile.Create(AppConfig.CreateDefault(), new RoomConfig { RoomId = 1, Quality = new StreamQuality(quality), CodecPriority = codecPriority, FormatPriority = formatPriority });
	}

	private static string Describe(IEnumerable<LiveStreamCandidate> candidates)
	{
		return string.Join(' ', candidates.Select(static candidate => $"{candidate.Format}/{candidate.Codec}@{candidate.Qn}"));
	}

	// 21622811 匿名时 AV1 是原画，AVC 是超清
	[Test]
	[Arguments(new[] { StreamCodec.Avc, StreamCodec.Hevc, StreamCodec.Av1 })]
	[Arguments(new[] { StreamCodec.Av1, StreamCodec.Avc, StreamCodec.Hevc })]
	public async Task HigherQualityComesFirstWhateverTheCodecPriorityAsync(StreamCodec[] codecPriority)
	{
		LiveStreamCandidate.Listed[] listed = [Listed(StreamCodec.Avc, 250), Listed(StreamCodec.Hevc, 250), Listed(StreamCodec.Av1, 10000)];

		await Assert.That(CandidateOrder.Sort(listed, Profile(10000, codecPriority))[0].Codec).IsEqualTo(StreamCodec.Av1);
	}

	[Test]
	public async Task SameQualityIsOrderedByCodecThenFormatPriorityAsync()
	{
		LiveStreamCandidate.Listed[] listed = [Listed(StreamCodec.Avc, 10000, StreamFormat.Flv), Listed(StreamCodec.Hevc, 10000, StreamFormat.Ts), Listed(StreamCodec.Avc, 10000), Listed(StreamCodec.Hevc, 10000, StreamFormat.Flv), Listed(StreamCodec.Avc, 400)];

		IReadOnlyList<LiveStreamCandidate.Listed> sorted = CandidateOrder.Sort(listed, Profile(10000, [StreamCodec.Hevc, StreamCodec.Avc, StreamCodec.Av1], [StreamFormat.Flv, StreamFormat.Fmp4, StreamFormat.Ts]));

		await Assert.That(Describe(sorted)).IsEqualTo("Flv/Hevc@10000 Ts/Hevc@10000 Flv/Avc@10000 Fmp4/Avc@10000 Fmp4/Avc@400");
	}

	[Test]
	[Arguments(10000L, "10000 400 250 25000", DisplayName = "不超过所选画质的越高越好，超过的排在后面")]
	[Arguments(25000L, "25000 10000 400 250", DisplayName = "所选画质在列")]
	[Arguments(20000L, "10000 400 250 25000", DisplayName = "所选画质不在列")]
	[Arguments(80L, "250 400 10000 25000", DisplayName = "都超过所选画质时越低越好")]
	public async Task QualityIsRankedAgainstTheSelectionAsync(long quality, string expected)
	{
		LiveStreamCandidate.Listed[] listed = [Listed(StreamCodec.Avc, 250), Listed(StreamCodec.Avc, 25000), Listed(StreamCodec.Avc, 10000), Listed(StreamCodec.Avc, 400)];

		await Assert.That(string.Join(' ', CandidateOrder.Sort(listed, Profile(quality)).Select(static candidate => candidate.Qn))).IsEqualTo(expected);
	}

	// 哔哩哔哩默认给什么录什么，只按编码和格式排
	[Test]
	public async Task DefaultQualityIsNotComparedAsync()
	{
		LiveStreamCandidate.Listed[] listed = [Listed(StreamCodec.Av1, 250), Listed(StreamCodec.Avc, 10000)];

		await Assert.That(Describe(CandidateOrder.Sort(listed, Profile(0)))).IsEqualTo("Fmp4/Avc@10000 Fmp4/Av1@250");
	}

	[Test]
	public async Task CodecsOutsideThePriorityKeepTheirOrderAsync()
	{
		LiveStreamCandidate.Listed[] listed = [Listed(StreamCodec.Av1, 10000), Listed(StreamCodec.Avc, 10000), Listed(StreamCodec.Hevc, 10000)];

		await Assert.That(Describe(CandidateOrder.Sort(listed, Profile(10000, [StreamCodec.Hevc])))).IsEqualTo("Fmp4/Hevc@10000 Fmp4/Av1@10000 Fmp4/Avc@10000");
	}

	[Test]
	public async Task SourceComesFirstAmongTheSameQualityAsync()
	{
		LiveStreamCandidate[] candidates = [Listed(StreamCodec.Avc, 10000, StreamFormat.Flv, "live_a_b_c_bluray"), Variant(StreamCodec.Hevc, 10000, "live_a_b_c")];

		await Assert.That(Describe(CandidateOrder.Sort(candidates, Profile(10000, [StreamCodec.Avc, StreamCodec.Hevc, StreamCodec.Av1], [StreamFormat.Flv, StreamFormat.Fmp4, StreamFormat.Ts])))).IsEqualTo("Fmp4/Hevc@10000 Flv/Avc@10000");
	}

	[Test]
	public async Task SourceDoesNotOutrankQualityAsync()
	{
		LiveStreamCandidate[] candidates = [Variant(StreamCodec.Avc, 25000, "live_a_b_c"), Variant(StreamCodec.Hevc, 10000, "live_a_b_c_prohevc"), Listed(StreamCodec.Avc, 10000, StreamFormat.Flv, "live_a_b_c_bluray")];

		await Assert.That(Describe(CandidateOrder.Sort(candidates, Profile(10000)))).IsEqualTo("Flv/Avc@10000 Fmp4/Hevc@10000 Fmp4/Avc@25000");
	}
}
