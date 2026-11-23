namespace BilibiliLive.Http;

// 接口请求、弹幕握手和弹幕认证包必须带同一个 buvid3
public sealed class BilibiliIdentity(Func<CancellationToken, ValueTask<BilibiliRequestOptions>> optionsProvider)
{
	private const string Buvid3CookieName = "buvid3";

	internal string GeneratedBuvid3 { get; } = Guid.NewGuid().ToString().ToUpperInvariant() + "infoc";

	internal ValueTask<BilibiliRequestOptions> GetOptionsAsync(CancellationToken cancellationToken)
	{
		return optionsProvider(cancellationToken);
	}

	internal string GetCookie(BilibiliRequestOptions options, bool anonymous)
	{
		return anonymous ? $"{Buvid3CookieName}={GetBuvid3(options)}" : CookieHeader.EnsureCookie(options.Cookie, Buvid3CookieName, GeneratedBuvid3);
	}

	internal async ValueTask<string> GetCookieAsync(CancellationToken cancellationToken)
	{
		return GetCookie(await optionsProvider(cancellationToken), anonymous: false);
	}

	internal async ValueTask<string> GetBuvid3Async(CancellationToken cancellationToken)
	{
		return GetBuvid3(await optionsProvider(cancellationToken));
	}

	// 和 EnsureCookie 一样，选项里已有的 buvid3 优先于生成的
	private string GetBuvid3(BilibiliRequestOptions options)
	{
		return CookieHeader.GetValue(options.Cookie, Buvid3CookieName) ?? GeneratedBuvid3;
	}
}
