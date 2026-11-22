namespace BilibiliLiveRecordDownLoader.Application.Http;

internal static class BilibiliHttpServiceCollectionExtensions
{
	public static IServiceCollection AddBilibiliHttpClients(this IServiceCollection services)
	{
		services.TryAddSingleton(static _ => new SwitchableWebProxy(HttpClient.DefaultProxy));
		services.TryAddSingleton(CreateIdentity);

		services.AddHttpClient(HttpClientNames.Default, static client => client.Timeout = TimeSpan.FromSeconds(15))
			.ConfigurePrimaryHttpMessageHandler(static provider => CreatePrimaryHandler(provider, allowAutoRedirect: true))
			.SetHandlerLifetime(Timeout.InfiniteTimeSpan);

		// 接口不需要跟随重定向；自动跟随时登录 Cookie 会被带到跳转后的主机
		services.AddHttpClient(HttpClientNames.BilibiliApi, static client => client.Timeout = TimeSpan.FromSeconds(15))
			.ConfigurePrimaryHttpMessageHandler(static provider => CreatePrimaryHandler(provider, allowAutoRedirect: false))
			.AddHttpMessageHandler(static provider => new BilibiliRequestHandler(provider.GetRequiredService<BilibiliIdentity>()))
			.SetHandlerLifetime(Timeout.InfiniteTimeSpan);

		// 直播流持续数小时，超时由录制流程按连接和空闲分别控制；CDN 会把请求重定向到其他节点，这条管道不带 Cookie
		services.AddHttpClient(HttpClientNames.BilibiliStream, static client => client.Timeout = Timeout.InfiniteTimeSpan)
			.ConfigurePrimaryHttpMessageHandler(static provider => CreatePrimaryHandler(provider, allowAutoRedirect: true))
			.AddHttpMessageHandler(static provider => new BilibiliRequestHandler(provider.GetRequiredService<BilibiliIdentity>()) { IncludeCookie = false })
			.SetHandlerLifetime(Timeout.InfiniteTimeSpan);

		services.TryAddSingleton(static provider => new BilibiliApiClient(provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientNames.BilibiliApi), provider.GetRequiredService<TimeProvider>()));
		services.TryAddSingleton(static provider => new DanmuClient(provider.GetRequiredService<BilibiliApiClient>(), provider.GetRequiredService<BilibiliIdentity>(), provider.GetRequiredService<ILogger<DanmuClient>>()));

		return services;
	}

	private static HttpMessageHandler CreatePrimaryHandler(IServiceProvider provider, bool allowAutoRedirect)
	{
		return new SocketsHttpHandler
		{
			Proxy = provider.GetRequiredService<SwitchableWebProxy>(),
			UseProxy = true,
			UseCookies = false,
			AllowAutoRedirect = allowAutoRedirect,
			AutomaticDecompression = DecompressionMethods.All,
			PooledConnectionLifetime = TimeSpan.FromMinutes(10)
		};
	}

	private static BilibiliIdentity CreateIdentity(IServiceProvider provider)
	{
		IAppConfigManager configManager = provider.GetRequiredService<IAppConfigManager>();

		return new BilibiliIdentity
		(async cancellationToken =>
			{
				AppConfig config = await configManager.GetAsync(cancellationToken);
				return new BilibiliRequestOptions(NullIfWhiteSpace(config.UserAgent), NullIfWhiteSpace(config.Cookie));
			}
		);

		static string? NullIfWhiteSpace(string? value)
		{
			return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
		}
	}
}
