using System.Net.Http.Headers;

namespace BilibiliLive.Http;

public sealed class BilibiliRequestHandler(BilibiliIdentity identity) : DelegatingHandler
{
	internal const string DefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36";

	internal const string DefaultAccept = "application/json, text/plain, */*";

	// ClientWebSocket 自己构造握手请求，设置不了 Options，只能用请求头要求匿名；这个请求头不会离开本处理器
	internal const string AnonymousHeader = "X-BilibiliLive-Anonymous";

	internal static readonly HttpRequestOptionsKey<bool> AnonymousOption = new("BilibiliLive.Anonymous");

	private static readonly Uri Referrer = new("https://live.bilibili.com/");

	// 自动重定向会把 Cookie 请求头带到新主机，跟随重定向的管道不能发送它
	public bool IncludeCookie { get; init; } = true;

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		HttpRequestHeaders headers = request.Headers;
		bool markedAnonymous = headers.Remove(AnonymousHeader);
		BilibiliRequestOptions options = await identity.GetOptionsAsync(cancellationToken);

		headers.Referrer = Referrer;

		headers.UserAgent.Clear();
		headers.TryAddWithoutValidation("User-Agent", ResolveUserAgent(options.UserAgent));

		headers.Remove("Cookie");

		// 直播流管道的每个分片都经过这里，不发送时就不拼 Cookie
		if (IncludeCookie && IsBilibiliHost(request.RequestUri))
		{
			bool anonymous = markedAnonymous || request.Options.TryGetValue(AnonymousOption, out bool value) && value;
			headers.TryAddWithoutValidation("Cookie", identity.GetCookie(options, anonymous));
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

	// 直播流来自 CDN 和第三方 PCDN 节点，登录 Cookie 只发往 B 站自己的域名
	private static bool IsBilibiliHost(Uri? uri)
	{
		return uri?.Host is { } host && (host.Equals("bilibili.com", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".bilibili.com", StringComparison.OrdinalIgnoreCase));
	}
}
