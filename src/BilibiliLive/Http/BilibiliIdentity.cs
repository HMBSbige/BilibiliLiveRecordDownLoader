namespace BilibiliLive.Http;

// API requests, the danmu handshake and the danmu auth packet have to present the same buvid3
public sealed class BilibiliIdentity(Func<CancellationToken, ValueTask<BilibiliRequestOptions>> optionsProvider)
{
	private const string Buvid3CookieName = "buvid3";

	internal string GeneratedBuvid3 { get; } = Guid.NewGuid().ToString().ToUpperInvariant() + "infoc";

	// Anonymous requests drop the login cookie but keep the buvid3, so the danmu auth packet still matches
	internal async ValueTask<(string? UserAgent, string Cookie)> ResolveAsync(bool anonymous, CancellationToken cancellationToken)
	{
		BilibiliRequestOptions options = await optionsProvider(cancellationToken);
		string cookie = anonymous ? $"{Buvid3CookieName}={GetBuvid3(options)}" : CookieHeader.EnsureCookie(options.Cookie, Buvid3CookieName, GeneratedBuvid3);
		return (options.UserAgent, cookie);
	}

	internal async ValueTask<string> GetBuvid3Async(CancellationToken cancellationToken)
	{
		return GetBuvid3(await optionsProvider(cancellationToken));
	}

	// Same precedence as EnsureCookie: a buvid3 in the user's cookie wins over the generated one
	private string GetBuvid3(BilibiliRequestOptions options)
	{
		return CookieHeader.GetValue(options.Cookie, Buvid3CookieName) ?? GeneratedBuvid3;
	}
}
