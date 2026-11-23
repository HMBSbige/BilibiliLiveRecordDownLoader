namespace BilibiliLiveRecordDownLoader.Application.Http;

internal static class BilibiliHttpServiceCollectionExtensions
{
	private static readonly TimeSpan ApiRequestTimeout = TimeSpan.FromSeconds(15);

	public static IServiceCollection AddBilibiliHttpClients(this IServiceCollection services)
	{
		services.TryAddSingleton(static _ => new SwitchableWebProxy(HttpClient.DefaultProxy));
		services.TryAddSingleton(CreateIdentity);

		AddClient(services, HttpClientNames.Default, ApiRequestTimeout, allowAutoRedirect: true);

		// 接口不需要跟随重定向；自动跟随时登录 Cookie 会被带到跳转后的主机
		AddClient(services, HttpClientNames.BilibiliApi, ApiRequestTimeout, allowAutoRedirect: false)
			.AddHttpMessageHandler(static provider => new BilibiliRequestHandler(provider.GetRequiredService<BilibiliIdentity>()));

		// 直播流持续数小时，超时由录制流程按连接和空闲分别控制；CDN 会把请求重定向到其他节点，这条管道不带 Cookie
		AddClient(services, HttpClientNames.BilibiliStream, Timeout.InfiniteTimeSpan, allowAutoRedirect: true)
			.AddHttpMessageHandler(static provider => new BilibiliRequestHandler(provider.GetRequiredService<BilibiliIdentity>()) { IncludeCookie = false });

		services.TryAddSingleton(static provider => new BilibiliApiClient(provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientNames.BilibiliApi), provider.GetRequiredService<TimeProvider>()));
		services.TryAddSingleton(static provider => new DanmuClient(provider.GetRequiredService<BilibiliApiClient>(), provider.GetRequiredService<BilibiliIdentity>(), provider.GetRequiredService<ILogger<DanmuClient>>()));

		return services;
	}

	private static IHttpClientBuilder AddClient(IServiceCollection services, string name, TimeSpan timeout, bool allowAutoRedirect)
	{
		return services.AddHttpClient(name, client => client.Timeout = timeout)
			.ConfigurePrimaryHttpMessageHandler(provider => new SocketsHttpHandler
			{
				Proxy = provider.GetRequiredService<SwitchableWebProxy>(),
				UseCookies = false,
				AllowAutoRedirect = allowAutoRedirect,
				AutomaticDecompression = DecompressionMethods.All,
				PooledConnectionLifetime = TimeSpan.FromMinutes(10)
			})
			.SetHandlerLifetime(Timeout.InfiniteTimeSpan);
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
