using BilibiliLive.Streams;

namespace BilibiliLive.Tests.Streams;

public class SourceStreamsTest
{
	private static LiveStreamCandidate.Listed Candidate(StreamProtocol protocol, StreamFormat format, StreamCodec codec, params string[] urls)
	{
		return new LiveStreamCandidate.Listed(protocol, format, codec, 250, [10000, 250], [.. urls.Select(static url => new Uri(url))]);
	}

	[Test]
	[Arguments("https://cn-gddg-cm-01-01.bilivideo.com/live-bvc/123/live_yx6maufasl_SWHHrSI_12ajzp_2500/index.m3u8?expires=1&len=0&uid=42&sign=x", "https://cn-gddg-cm-01-01.bilivideo.com/live-bvc/123/live_yx6maufasl_SWHHrSI_12ajzp/index.m3u8?expires=1&len=0&uid=42&sign=x")]
	[Arguments("https://cdn.test/live-bvc/1/live_a_b_c_minihevc/index.m3u8?sign=x", "https://cdn.test/live-bvc/1/live_a_b_c/index.m3u8?sign=x")]
	[Arguments("https://cdn.test/live-bvc/1/live_a_b_c_proav1/index.m3u8", "https://cdn.test/live-bvc/1/live_a_b_c/index.m3u8")]
	[Arguments("https://cdn.test/live-bvc/1/live_a_b_c_2500.m3u8?sign=x", "https://cdn.test/live-bvc/1/live_a_b_c.m3u8?sign=x")]
	// These nodes answer with a master playlist, under the same kind of path
	[Arguments("https://d1--cn-gotcha208.bilivideo.com/live-bvc/1/live_a_b_c_2500/index.m3u8?sign=x&trid=y", "https://d1--cn-gotcha208.bilivideo.com/live-bvc/1/live_a_b_c/index.m3u8?sign=x&trid=y")]
	public async Task TranscodingSuffixIsRemovedAndTheQueryKeptAsync(string transcoded, string expected)
	{
		await Assert.That(SourceStreams.TryGetSourceUri(new Uri(transcoded))?.OriginalString).IsEqualTo(expected);
	}

	[Test]
	// Already the four-part name of the original
	[Arguments("https://cdn.test/live-bvc/1/live_a_b_c/index.m3u8?sign=x")]
	// Four-part names were also seen at qn 150 and 250, where any part removed would be a guess
	[Arguments("https://cdn.test/live-bvc/1/live_a_b_2500/index.m3u8?sign=x")]
	[Arguments("https://cdn.test/live-bvc/1/stream_a_b_c_2500/index.m3u8?sign=x")]
	[Arguments("https://cdn.test/live-bvc/1/live_a_b_c_d_2500/index.m3u8?sign=x")]
	[Arguments("https://cdn.test/live-bvc/1/live_a__c_2500/index.m3u8?sign=x")]
	[Arguments("https://cdn.test/live-bvc/1/live_a_b_c_bluray/index.m3u8?sign=x")]
	[Arguments("https://cdn.test/live_a_b_c_2500/live_d_e_f_2500/index.m3u8?sign=x")]
	public async Task OtherStreamNamesAreLeftAloneAsync(string url)
	{
		await Assert.That(SourceStreams.TryGetSourceUri(new Uri(url))).IsNull();
	}

	[Test]
	public async Task OnlyFmp4IsDerivedAvcFirstAndEachPathOnceAsync()
	{
		LiveStreamCandidate.Listed[] listed =
		[
			Candidate(StreamProtocol.HttpStream, StreamFormat.Flv, StreamCodec.Avc, "https://flv.test/live-bvc/1/live_a_b_c_2500.flv?sign=1"),
			Candidate(StreamProtocol.HttpHls, StreamFormat.Ts, StreamCodec.Avc, "https://ts.test/live-bvc/1/live_a_b_c_2500/index.m3u8?sign=2"),
			Candidate(StreamProtocol.HttpHls, StreamFormat.Fmp4, StreamCodec.Hevc, "https://a.test/live-bvc/1/live_a_b_c_minihevc/index.m3u8?sign=hevc", "https://c.test/live-bvc/1/live_a_b_c_minihevc/index.m3u8?sign=hevc"),
			Candidate(StreamProtocol.HttpHls, StreamFormat.Fmp4, StreamCodec.Av1, "https://a.test/live-bvc/1/live_a_b_c_proav1/index.m3u8?sign=av1"),
			Candidate(StreamProtocol.HttpHls, StreamFormat.Fmp4, StreamCodec.Avc, "https://a.test/live-bvc/1/live_a_b_c_2500/index.m3u8?sign=avc", "https://b.test/live-bvc/1/live_a_b_c_2500/index.m3u8?sign=avc"),
			Candidate(StreamProtocol.HttpHls, StreamFormat.Fmp4, StreamCodec.Hevc, "https://d.test/live-bvc/1/live_a_b_c/index.m3u8?sign=original")
		];

		IReadOnlyList<LiveStreamCandidate.Source> sources = SourceStreams.Derive(listed);

		using (Assert.Multiple())
		{
			await Assert.That(sources.All(static source => source is { Protocol: StreamProtocol.HttpHls, Format: StreamFormat.Fmp4, Qn: LiveStreamCandidate.OriginalQuality })).IsTrue();
			await Sequences.AssertEqualAsync(sources.Select(static source => string.Join(" ", source.Urls.Select(static uri => uri.OriginalString))),
			[
				"https://a.test/live-bvc/1/live_a_b_c/index.m3u8?sign=avc https://b.test/live-bvc/1/live_a_b_c/index.m3u8?sign=avc",
				"https://c.test/live-bvc/1/live_a_b_c/index.m3u8?sign=hevc"
			]);
		}
	}
}
