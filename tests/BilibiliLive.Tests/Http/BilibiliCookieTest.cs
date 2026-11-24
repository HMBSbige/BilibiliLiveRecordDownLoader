using BilibiliLive.Http;

namespace BilibiliLive.Tests.Http;

public class BilibiliCookieTest
{
	[Test]
	[Arguments("buvid3=B; SESSDATA=0a1b%2C1795000000%2Cc2d3*11", 1_795_000_000L)]
	[Arguments("SESSDATA=abc", null)]
	[Arguments("SESSDATA=a%2Cx%2Cb", null)]
	[Arguments("SESSDATA=a%2C-1%2Cb", null)]
	[Arguments("SESSDATA=a%2C99999999999999999%2Cb", null)]
	[Arguments(null, null)]
	public async Task GetSessionExpiryReadsTheSecondSegmentAsync(string? cookie, long? expectedSeconds)
	{
		DateTimeOffset? expected = expectedSeconds is { } seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
		await Assert.That(BilibiliCookie.GetSessionExpiry(cookie)).IsEqualTo(expected);
	}
}
