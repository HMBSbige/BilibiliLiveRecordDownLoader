using BilibiliLive.Api;

namespace BilibiliLive.Tests.Api;

public class LiveStreamNameTest
{
	[Test]
	[Arguments("/live-bvc/232914/live_2cnsl_Sht3kFN_1335ay_2500.flv", "live_2cnsl_Sht3kFN_1335ay_2500")]
	[Arguments("/live-bvc/335010/live_2cnsl_Sht3kFN_1335ay_2500.m3u8", "live_2cnsl_Sht3kFN_1335ay_2500")]
	[Arguments("/live-bvc/272696/live_2cnsl_Sht3kFN_1335ay/index.m3u8", "live_2cnsl_Sht3kFN_1335ay")]
	[Arguments("/live/index.m3u8", null, DisplayName = "不是 live-bvc 路径")]
	[Arguments("/live-bvc/1/a/live_2500.flv", null, DisplayName = "流名前多一级目录")]
	public async Task NameComesFromTheStreamPathAsync(string path, string? expected)
	{
		await Assert.That(LiveStreamName.Parse(path)).IsEqualTo(expected);
	}
}
