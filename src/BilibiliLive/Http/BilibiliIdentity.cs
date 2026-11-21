namespace BilibiliLive.Http;

// API requests, the danmu handshake and the danmu auth packet have to present the same buvid3
public sealed class BilibiliIdentity(Func<CancellationToken, ValueTask<BilibiliRequestOptions>> optionsProvider)
{
	private const string Buvid3CookieName = "buvid3";

	internal string GeneratedBuvid3 { get; } = Guid.NewGuid().ToString().ToUpperInvariant() + "infoc";

	internal async ValueTask<(string? UserAgent, string Cookie)> ResolveAsync(CancellationToken cancellationToken)
	{
		BilibiliRequestOptions options = await optionsProvider(cancellationToken);
		return (options.UserAgent, CookieHeader.EnsureCookie(options.Cookie, Buvid3CookieName, GeneratedBuvid3));
	}

	// Same precedence as ResolveAsync: a buvid3 in the user's cookie wins over the generated one
	internal async ValueTask<string> GetBuvid3Async(CancellationToken cancellationToken)
	{
		BilibiliRequestOptions options = await optionsProvider(cancellationToken);
		return CookieHeader.GetValue(options.Cookie, Buvid3CookieName) ?? GeneratedBuvid3;
	}
}
