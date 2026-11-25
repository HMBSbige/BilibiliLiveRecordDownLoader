using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using System.Net;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

// 过滤器只认对应的编码：只有 TS 里的 AAC 带 ADTS 头，转换参数集的过滤器各编码各一个，用错了 FFmpeg 会启动失败
public sealed class FFmpegStreamFilterTest
{
	private const string Playlist = "/live/avc/index.m3u8";

	private const string TransportStreams = $$"""[{"protocol_name":"http_hls","format":[{"format_name":"ts","codec":[{"codec_name":"avc","current_qn":10000,"base_url":"{{Playlist}}","url_info":[{"host":"https://a.test","extra":"?sign=$SIGN"}]}]}]}]""";

	private const string FlvStreams = """[{"protocol_name":"http_stream","format":[{"format_name":"flv","codec":[{"codec_name":"$CODEC","current_qn":10000,"base_url":"/live/x.flv","url_info":[{"host":"https://a.test","extra":"?sign=$SIGN"}]}]}]}]""";

	[Test]
	public async Task TransportStreamGetsItsAudioAndVideoConvertedAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken, recorderType: RecorderType.FFmpeg, streams: TransportStreams, respondStream: RespondTransportStream);
		loop.GoLive();

		await loop.WaitUntilAsync(() => !loop.FFmpegRuns.IsEmpty, "FFmpeg is started");
		string[] arguments = [.. loop.FFmpegRuns.First()];

		using (Assert.Multiple())
		{
			await Assert.That(ValueOf(arguments, "-bsf:a")).IsEqualTo("aac_adtstoasc");
			await Assert.That(ValueOf(arguments, "-bsf:v")).IsEqualTo("h264_mp4toannexb");
		}
	}

	[Test]
	[Arguments("avc", "h264_mp4toannexb")]
	[Arguments("hevc", "hevc_mp4toannexb")]
	[Arguments("av1", null)]
	public async Task FlvStreamGetsTheVideoFilterOfItsCodecAsync(string codec, string? videoFilter, CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken, recorderType: RecorderType.FFmpeg, streams: FlvStreams.Replace("$CODEC", codec, StringComparison.Ordinal));
		loop.GoLive();
		StreamRequest request = await loop.NextStreamAsync();
		await request.AcceptAsync();

		await loop.WaitUntilAsync(() => !loop.FFmpegRuns.IsEmpty, "FFmpeg is started");
		string[] arguments = [.. loop.FFmpegRuns.First()];

		using (Assert.Multiple())
		{
			await Assert.That(ValueOf(arguments, "-bsf:v")).IsEqualTo(videoFilter);
			await Assert.That(arguments).DoesNotContain("-bsf:a");
		}
	}

	private static string? ValueOf(string[] arguments, string option)
	{
		int index = Array.IndexOf(arguments, option);
		return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
	}

	private static HttpResponseMessage RespondTransportStream(HttpRequestMessage request)
	{
		return request.RequestUri?.AbsolutePath switch
		{
			Playlist => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXTINF:1,\n1.ts\n", Encoding.UTF8, "application/vnd.apple.mpegurl") },
			"/live/avc/1.ts" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([.. TsSamples.Pat(), .. TsSamples.Pmt(), .. TsSamples.VideoFrame(TsSamples.AvcIdr, randomAccess: true)]) },
			_ => new HttpResponseMessage(HttpStatusCode.NotFound)
		};
	}
}
