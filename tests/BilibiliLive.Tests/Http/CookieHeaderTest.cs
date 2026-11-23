using BilibiliLive.Http;

namespace BilibiliLive.Tests.Http;

public class CookieHeaderTest
{
	[Test]
	[Arguments(" a=1; b=2; ", "a=1; b=2; buvid3=V")]
	[Arguments("xbuvid3=1; a=buvid3", "xbuvid3=1; a=buvid3; buvid3=V")]
	[Arguments("a=1; buvid3 = X", "a=1; buvid3 = X")]
	public async Task EnsureCookieAddsOnlyMissingNameAsync(string? cookie, string expected)
	{
		await Assert.That(CookieHeader.EnsureCookie(cookie, "buvid3", "V")).IsEqualTo(expected);
	}

	[Test]
	[Arguments("a=1; buvid3 = X ; b=2", "X")]
	[Arguments("buvid3=first; buvid3=second", "first")]
	[Arguments("buvid3=", "")]
	public async Task GetValueMatchesWholeNamesOnlyAsync(string? cookie, string? expected)
	{
		await Assert.That(CookieHeader.GetValue(cookie, "buvid3")).IsEqualTo(expected);
	}

	[Test]
	public async Task FromSetCookieKeepsNameValuePairsAndLastValueWinsAsync()
	{
		string[] headers =
		[
			"SESSDATA=old%2Cvalue; Path=/; Domain=bilibili.com; Expires=Sun, 18 May 2027 10:00:00 GMT; HttpOnly; Secure",
			"bili_jct=csrf; Path=/; Domain=bilibili.com",
			"DedeUserID=12345678; Path=/",
			"invalid-without-separator; Path=/",
			"SESSDATA=new%2Cvalue; Path=/"
		];

		await Assert.That(CookieHeader.FromSetCookie(headers)).IsEqualTo("SESSDATA=new%2Cvalue; bili_jct=csrf; DedeUserID=12345678");
	}
}
