using System.Globalization;

namespace BilibiliLive.Http;

public static class BilibiliCookie
{
	internal const string SessionName = "SESSDATA";

	private const string CsrfName = "bili_jct";

	private static readonly long MaxUnixSeconds = DateTimeOffset.MaxValue.ToUnixTimeSeconds();

	/// <param name="updates">登录或续期响应设置的条目</param>
	public static string Merge(string? cookie, string updates)
	{
		return CookieHeader.Merge(cookie, updates);
	}

	public static bool IsSameSession(string? x, string? y)
	{
		return CookieHeader.GetValue(x, SessionName) == CookieHeader.GetValue(y, SessionName);
	}

	// URL 解码后为 {hex},{过期时间的 Unix 秒},{...}，第二段与 Set-Cookie 的 Expires 一致；格式对不上时当作不知道
	public static DateTimeOffset? GetSessionExpiry(string? cookie)
	{
		return CookieHeader.GetValue(cookie, SessionName) is { } session
			&& Uri.UnescapeDataString(session).Split(',') is [_, var seconds, _, ..]
			&& long.TryParse(seconds, NumberStyles.None, CultureInfo.InvariantCulture, out long unixSeconds)
			&& unixSeconds <= MaxUnixSeconds
				? DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
				: null;
	}

	internal static string GetCsrf(string cookie)
	{
		return CookieHeader.GetValue(cookie, CsrfName) is { Length: > 0 } csrf ? csrf : throw new BilibiliApiException($"The cookie contains no {CsrfName}.");
	}
}
