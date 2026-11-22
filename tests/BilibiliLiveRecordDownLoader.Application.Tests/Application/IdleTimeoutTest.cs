using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using System.Net;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class IdleTimeoutTest
{
	private const string HlsStreams = """[{"protocol_name":"http_hls","format":[{"format_name":"fmp4","codec":[{"codec_name":"avc","current_qn":10000,"accept_qn":[10000],"base_url":"/live/index.m3u8","url_info":[{"host":"https://a.test","extra":"?sign=$SIGN"}]}]}]}]""";

	private const string StalledPlaylist = "#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXT-X-MAP:URI=\"h1.m4s\"\n#EXTINF:1,\n1.m4s\n";

	[Test]
	[Arguments(10, null, 10)]
	[Arguments(10, 18.5, 19.5)]
	[Arguments(30, 18.5, 30)]
	public async Task StreamsThatGoSilentBetweenSegmentsWaitLongerThanTheSettingAsync(int configuredSeconds, double? maxSilenceSeconds, double expectedSeconds)
	{
		TimeSpan? maxSilence = maxSilenceSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null;

		await Assert.That(LiveRecorder.GetIdleTimeout(TimeSpan.FromSeconds(configuredSeconds), maxSilence)).IsEqualTo(TimeSpan.FromSeconds(expectedSeconds));
	}

	// FFmpeg 的会话不知道分片间隔，空闲超时要按验证线路时的 HLS 会话算
	[Test]
	[Timeout(30_000)]
	[Arguments(RecorderType.BuiltIn)]
	[Arguments(RecorderType.FFmpeg)]
	public async Task StalledHlsStreamWaitsTheSilenceBetweenSegmentsAsync(RecorderType recorderType, CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken, recorderType: recorderType, streams: HlsStreams, respondStream: RespondHls);

		if (recorderType is RecorderType.FFmpeg)
		{
			WriteFFmpegThatWaitsForQuit(loop);
		}

		// 分片间隔 2 秒加 1 秒刷新；请求超时 2 秒、正文超时 4 秒，用满两段超时再重试一次要 13 秒：MaxSilence 是 16 秒，空闲超时 17 秒
		loop.Settings.StreamConnectTimeout = TimeSpan.FromSeconds(2);
		loop.Settings.StreamIdleTimeout = TimeSpan.FromSeconds(3);
		loop.GoLive();
		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Recording);

		for (int step = 0; step < 60 && loop.Snapshot.LastError is null; ++step)
		{
			loop.Time.Advance(TimeSpan.FromSeconds(1));
			await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
		}

		// 负载高时时钟可能先推过了空闲超时，录制循环还没处理完
		await loop.WaitUntilAsync(static snapshot => snapshot.LastError is not null);
		await Assert.That(loop.Snapshot.LastError).Contains(AppLanguage.Current.RecordingStreamIdle.ToString("17"));
	}

	private static HttpResponseMessage RespondHls(HttpRequestMessage request)
	{
		return request.RequestUri?.AbsolutePath switch
		{
			"/live/index.m3u8" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(StalledPlaylist, Encoding.UTF8, "application/vnd.apple.mpegurl") },
			"/live/h1.m4s" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(FillerSamples.Init(FillerCodec.Avc, 4)) },
			"/live/1.m4s" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Fmp4Samples.Fragment(true)) },
			_ => new HttpResponseMessage(HttpStatusCode.NotFound)
		};
	}

	private static void WriteFFmpegThatWaitsForQuit(RecordingLoopHarness loop)
	{
		string path = loop.Config.FFmpegPath ?? throw new InvalidOperationException("The harness is not recording with FFmpeg.");
		File.WriteAllText(path, OperatingSystem.IsWindows() ? "@echo ts-data\r\n@pause >nul\r\n" : "#!/bin/sh\necho ts-data\nhead -c 1 >/dev/null\n");
	}
}
