using BilibiliLive.Api;
using BilibiliLive.Tests.Fakes;

namespace BilibiliLive.Tests.Api;

public class MasterPlaylistVariantsTest
{
	private static readonly Uri PlaylistUri = new("https://api.live.bilibili.com/xlive/play-gateway/master/url?cid=1");

	private static string Hosts(LiveStreamCandidate.Variant variant)
	{
		return string.Join(' ', variant.Urls.Select(static uri => uri.Host));
	}

	[Test]
	public async Task EntriesOfTheSameQualityAndCodecAreMergedAcrossPathwaysAsync()
	{
		LiveStreamCandidate.Variant[] variants = MasterPlaylistVariants.Parse(Responses.FixtureText("master_playlist.m3u8"), PlaylistUri);

		(StreamCodec, long, string?)[] expected = [(StreamCodec.Hevc, 10000, "原画"), (StreamCodec.Avc, 10000, "原画"), (StreamCodec.Hevc, 400, "蓝光"), (StreamCodec.Avc, 400, "蓝光"), (StreamCodec.Hevc, 250, "超清"), (StreamCodec.Avc, 250, "超清"), (StreamCodec.Hevc, 25000, "原画真彩")];
		LiveStreamCandidate.Variant originalTrueColor = variants[^1];

		using (Assert.Multiple())
		{
			await Assert.That(variants.Select(static variant => (variant.Codec, variant.Qn, variant.QualityName))).IsEquivalentTo(expected, CollectionOrdering.Matching);
			await Assert.That(variants).All(static variant => variant is { Protocol: StreamProtocol.HttpHls, Format: StreamFormat.Fmp4 });
			await Assert.That(variants.Select(Hosts)).All(static hosts => hosts == "d1--cn-gotcha208.bilivideo.com d1--cn-gotcha208b.bilivideo.com");
			await Assert.That(originalTrueColor.Urls[0].AbsoluteUri).IsEqualTo("https://d1--cn-gotcha208.bilivideo.com/live-bvc/495069/live_6650029_bs_9162320/index.m3u8?expires=1700000000&mid=0&sign=fake");
		}
	}

	[Test]
	public async Task SingleCodecPlaylistGivesOneVariantAsync()
	{
		LiveStreamCandidate.Variant variant = await Assert.That(MasterPlaylistVariants.Parse(Responses.FixtureText("master_playlist_avc.m3u8"), PlaylistUri)).HasSingleItem();

		await Assert.That((variant.Codec, variant.Qn, Hosts(variant))).IsEqualTo((StreamCodec.Avc, 10000, "cn-jsyz-ct-03-101.bilivideo.com d1--cn-gotcha204b.bilivideo.com"));
	}

	[Test]
	[Arguments("avc3.640028,mp4a.40.2", StreamCodec.Avc)]
	[Arguments("hev1.1.6.L150,mp4a.40.2", StreamCodec.Hevc)]
	[Arguments("av01.0.08M.08,mp4a.40.2", StreamCodec.Av1)]
	[Arguments(" hvc1.1.2.H150 , mp4a.40.2", StreamCodec.Hevc)]
	public async Task VideoCodecComesFromTheFirstCodecAsync(string codecs, StreamCodec expected)
	{
		LiveStreamCandidate.Variant variant = await Assert.That(MasterPlaylistVariants.Parse($"#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1,CODECS=\"{codecs}\",BILI-QN=10000\nhttps://a.test/live/index.m3u8\n", PlaylistUri)).HasSingleItem();

		await Assert.That(variant.Codec).IsEqualTo(expected);
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

		LiveStreamCandidate.Variant variant = await Assert.That(MasterPlaylistVariants.Parse(content, PlaylistUri)).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That((variant.Codec, variant.Qn, variant.QualityName)).IsEqualTo((StreamCodec.Avc, 250, null));
			await Assert.That(variant.Urls.Select(static uri => uri.AbsoluteUri)).IsEquivalentTo(["https://api.live.bilibili.com/xlive/play-gateway/master/relative/index.m3u8?sign=x"], CollectionOrdering.Matching).Because("相对地址按播放列表的地址解析，同一个地址只保留一次");
		}
	}

	[Test]
	[Arguments("")]
	[Arguments("""{"code":-400,"message":"请求错误","ttl":1}""")]
	public async Task ContentThatIsNotAPlaylistIsRejectedAsync(string content)
	{
		await Assert.That(() => MasterPlaylistVariants.Parse(content, PlaylistUri)).Throws<FormatException>();
	}
}
