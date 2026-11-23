using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using System.Net;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class FFmpegProxyTest
{
	private const string HttpProxy = "http://proxy.test:8080/";

	[Test]
	public async Task FFmpegRecordsThroughTheSystemProxyWhenTurnedOnAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken, recorderType: RecorderType.FFmpeg, systemProxy: new WebProxy(HttpProxy));
		loop.Config.UseSystemProxy = true;

		string[] arguments = await StartFFmpegAsync(loop);
		int proxy = Array.IndexOf(arguments, "-http_proxy");

		using (Assert.Multiple())
		{
			await Assert.That(proxy).IsGreaterThanOrEqualTo(0).And.IsLessThan(Array.IndexOf(arguments, "-i"));
			await Assert.That(arguments[proxy + 1]).IsEqualTo(HttpProxy);
		}
	}

	[Test]
	public async Task FFmpegConnectsDirectlyWhenTheSystemProxyIsTurnedOffAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken, recorderType: RecorderType.FFmpeg, systemProxy: new WebProxy(HttpProxy));

		string[] arguments = await StartFFmpegAsync(loop);

		await Assert.That(arguments).DoesNotContain("-http_proxy");
	}

	[Test]
	public async Task FFmpegConnectsDirectlyToABypassedHostAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken, recorderType: RecorderType.FFmpeg, systemProxy: new WebProxy(HttpProxy, false, [@"a\.test"]));
		loop.Config.UseSystemProxy = true;

		string[] arguments = await StartFFmpegAsync(loop);

		await Assert.That(arguments).DoesNotContain("-http_proxy");
	}

	[Test]
	public async Task FFmpegConnectsDirectlyAndSaysSoWhenTheSystemProxyIsNotHttpAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken, recorderType: RecorderType.FFmpeg, systemProxy: new WebProxy("socks5://proxy.test:1080"));
		loop.Config.UseSystemProxy = true;

		string[] arguments = await StartFFmpegAsync(loop);

		using (Assert.Multiple())
		{
			await Assert.That(arguments).DoesNotContain("-http_proxy");
			await Assert.That(loop.RecorderLogs).Contains("FFmpeg cannot use a socks5 proxy, connecting to a.test directly");
		}
	}

	private static async Task<string[]> StartFFmpegAsync(RecordingLoopHarness loop)
	{
		loop.GoLive();
		StreamRequest request = await loop.NextStreamAsync();
		await request.AcceptAsync();
		await loop.WaitUntilAsync(() => !loop.FFmpegRuns.IsEmpty, "FFmpeg is started");

		return [.. loop.FFmpegRuns.First()];
	}
}
