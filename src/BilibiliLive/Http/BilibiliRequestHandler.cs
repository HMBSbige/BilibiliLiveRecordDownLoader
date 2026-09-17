using System.Net.Http.Headers;

namespace BilibiliLive.Http;

public sealed class BilibiliRequestHandler(Func<CancellationToken, ValueTask<BilibiliRequestOptions>> optionsProvider) : DelegatingHandler
{
	internal const string DefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36";

	internal const string DefaultAccept = "application/json, text/plain, */*";

	private static readonly Uri Referrer = new("https://live.bilibili.com/");

	internal string Buvid3 { get; } = Guid.NewGuid().ToString().ToUpperInvariant() + "infoc";

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		BilibiliRequestOptions options = await optionsProvider(cancellationToken);
		HttpRequestHeaders headers = request.Headers;

		headers.Referrer = Referrer;

		headers.UserAgent.Clear();
		headers.TryAddWithoutValidation("User-Agent", ResolveUserAgent(options.UserAgent));

		headers.Remove("Cookie");

		if (IsBilibiliHost(request.RequestUri))
		{
			headers.TryAddWithoutValidation("Cookie", CookieHeader.EnsureCookie(options.Cookie, "buvid3", Buvid3));
		}

		if (headers.Accept.Count is 0)
		{
			headers.TryAddWithoutValidation("Accept", DefaultAccept);
		}

		return await base.SendAsync(request, cancellationToken);
	}

	public static string ResolveUserAgent(string? userAgent)
	{
		return userAgent?.Trim() is { Length: > 0 } value ? value : DefaultUserAgent;
	}

	// 直播流来自 CDN 和第三方 PCDN 节点，登录 Cookie 只发给 B 站自己的域名
	private static bool IsBilibiliHost(Uri? uri)
	{
		return uri?.Host is { } host && (host.Equals("bilibili.com", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".bilibili.com", StringComparison.OrdinalIgnoreCase));
	}
}
