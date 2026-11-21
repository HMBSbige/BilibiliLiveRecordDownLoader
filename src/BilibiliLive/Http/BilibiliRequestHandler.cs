using System.Net.Http.Headers;

namespace BilibiliLive.Http;

public sealed class BilibiliRequestHandler(BilibiliIdentity identity) : DelegatingHandler
{
	internal const string DefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36";

	internal const string DefaultAccept = "application/json, text/plain, */*";

	private static readonly Uri Referrer = new("https://live.bilibili.com/");

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		(string? userAgent, string cookie) = await identity.ResolveAsync(cancellationToken);
		HttpRequestHeaders headers = request.Headers;

		headers.Referrer = Referrer;

		headers.UserAgent.Clear();
		headers.TryAddWithoutValidation("User-Agent", ResolveUserAgent(userAgent));

		headers.Remove("Cookie");

		if (IsBilibiliHost(request.RequestUri))
		{
			headers.TryAddWithoutValidation("Cookie", cookie);
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

	// Streams come from CDN and third-party PCDN nodes, the login cookie only goes to Bilibili's own domains
	private static bool IsBilibiliHost(Uri? uri)
	{
		return uri?.Host is { } host && (host.Equals("bilibili.com", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".bilibili.com", StringComparison.OrdinalIgnoreCase));
	}
}
