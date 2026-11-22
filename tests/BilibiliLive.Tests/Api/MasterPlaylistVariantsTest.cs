using BilibiliLive.Api;

namespace BilibiliLive.Tests.Api;

public class MasterPlaylistVariantsTest
{
	private static readonly Uri PlaylistUri = new("https://api.live.bilibili.com/xlive/play-gateway/master/url?cid=1");

	private static string Fixture(string name)
	{
		return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
	}

	private static string Hosts(LiveStreamCandidate.Variant variant)
	{
		return string.Join(' ', variant.Urls.Select(static uri => uri.Host));
	}

	[Test]
	public async Task EntriesOfTheSameQualityAndCodecAreMergedAcrossPathwaysAsync()
	{
		LiveStreamCandidate.Variant[] variants = MasterPlaylistVariants.Parse(Fixture("master_playlist.m3u8"), PlaylistUri);

		(StreamCodec, long)[] expected = [(StreamCodec.Hevc, 10000), (StreamCodec.Avc, 10000), (StreamCodec.Hevc, 400), (StreamCodec.Avc, 400), (StreamCodec.Hevc, 250), (StreamCodec.Avc, 250), (StreamCodec.Hevc, 25000)];
		LiveStreamCandidate.Variant trueOriginal = variants[^1];

		using (Assert.Multiple())
		{
			await Sequences.AssertEqualAsync(variants.Select(static variant => (variant.Codec, variant.Qn)), expected);
			await Assert.That(variants.All(static variant => variant is { Protocol: StreamProtocol.HttpHls, Format: StreamFormat.Fmp4 })).IsTrue();
			await Assert.That(variants.Select(Hosts).Distinct().Single()).IsEqualTo("d1--cn-gotcha208.bilivideo.com d1--cn-gotcha208b.bilivideo.com");
			await Assert.That(trueOriginal.Urls[0].AbsoluteUri).IsEqualTo("https://d1--cn-gotcha208.bilivideo.com/live-bvc/495069/live_6650029_bs_9162320/index.m3u8?expires=1700000000&mid=0&sign=fake");
		}
	}

	[Test]
	public async Task SingleCodecPlaylistGivesOneVariantAsync()
	{
		LiveStreamCandidate.Variant[] variants = MasterPlaylistVariants.Parse(Fixture("master_playlist_avc.m3u8"), PlaylistUri);

		using (Assert.Multiple())
		{
			await Assert.That(variants.Length).IsEqualTo(1);
			await Assert.That(variants[0] is { Codec: StreamCodec.Avc, Qn: 10000 }).IsTrue();
			await Assert.That(Hosts(variants[0])).IsEqualTo("cn-jsyz-ct-03-101.bilivideo.com d1--cn-gotcha204b.bilivideo.com");
		}
	}

	[Test]
	[Arguments("avc1.640032,avc1.640032", StreamCodec.Avc)]
	[Arguments("avc3.640028,mp4a.40.2", StreamCodec.Avc)]
	[Arguments("hvc1.1.6.L150,hvc1.1.6.L150", StreamCodec.Hevc)]
	[Arguments("hev1.1.6.L150,mp4a.40.2", StreamCodec.Hevc)]
	[Arguments("av01.0.08M.08,mp4a.40.2", StreamCodec.Av1)]
	[Arguments(" hvc1.1.2.H150 , mp4a.40.2", StreamCodec.Hevc)]
	public async Task VideoCodecComesFromTheFirstCodecAsync(string codecs, StreamCodec expected)
	{
		LiveStreamCandidate.Variant[] variants = MasterPlaylistVariants.Parse($"#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1,CODECS=\"{codecs}\",BILI-QN=10000\nhttps://a.test/live/index.m3u8\n", PlaylistUri);

		await Assert.That(variants.Single().Codec).IsEqualTo(expected);
	}

	[Test]
	public async Task EntriesThatCannotBeRecordedAreLeftOutAsync()
	{
		const string content = """
			#EXTM3U
			#EXT-X-STREAM-INF:BANDWIDTH=1,CODECS="mp4a.40.2,avc1.640032",BILI-QN=10000
			https://a.test/audio-first/index.m3u8
			#EXT-X-STREAM-INF:BANDWIDTH=1,CODECS="vp09.00.10.08",BILI-QN=10000
			https://a.test/vp9/index.m3u8
			#EXT-X-STREAM-INF:BANDWIDTH=1,BILI-QN=10000
			https://a.test/no-codecs/index.m3u8
			#EXT-X-STREAM-INF:BANDWIDTH=1,CODECS="avc1.640032",BILI-DISPLAY="原画"
			https://a.test/no-quality/index.m3u8
			#EXT-X-STREAM-INF:BANDWIDTH=1,CODECS="avc1.640032",BILI-QN=400
			http://a.test/plain-http/index.m3u8
			https://a.test/not-after-stream-inf/index.m3u8
			#EXT-X-STREAM-INF:BANDWIDTH=1,CODECS="avc1.640032",BILI-QN=250
			relative/index.m3u8?sign=x
			#EXT-X-STREAM-INF:BANDWIDTH=1,CODECS="avc1.640032",BILI-QN=250
			https://api.live.bilibili.com/xlive/play-gateway/master/relative/index.m3u8?sign=x
			""";

		LiveStreamCandidate.Variant[] variants = MasterPlaylistVariants.Parse(content, PlaylistUri);

		using (Assert.Multiple())
		{
			await Assert.That(variants.Length).IsEqualTo(1);
			await Assert.That(variants[0] is { Codec: StreamCodec.Avc, Qn: 250 }).IsTrue();
			// A relative address resolves against the playlist, and the same address is kept once
			await Sequences.AssertEqualAsync(variants[0].Urls.Select(static uri => uri.AbsoluteUri), ["https://api.live.bilibili.com/xlive/play-gateway/master/relative/index.m3u8?sign=x"]);
		}
	}

	[Test]
	public async Task MediaPlaylistListsNoVariantAsync()
	{
		await Assert.That(MasterPlaylistVariants.Parse("#EXTM3U\n#EXT-X-TARGETDURATION:1\n#EXTINF:1,\n1.m4s\n", PlaylistUri)).IsEmpty();
	}

	[Test]
	// A room that is not live gets an empty body
	[Arguments("")]
	[Arguments("""{"code":-400,"message":"请求错误","ttl":1}""")]
	public async Task ContentThatIsNotAPlaylistIsRejectedAsync(string content)
	{
		await Assert.ThrowsAsync<FormatException>(() => Task.FromResult(MasterPlaylistVariants.Parse(content, PlaylistUri)));
	}
}
