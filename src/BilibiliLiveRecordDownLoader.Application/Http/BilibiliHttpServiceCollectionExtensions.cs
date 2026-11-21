namespace BilibiliLiveRecordDownLoader.Application.Http;

internal static class BilibiliHttpServiceCollectionExtensions
{
	public static IServiceCollection AddBilibiliHttpClients(this IServiceCollection services)
	{
		services.TryAddSingleton<SwitchableWebProxy>();
		services.TryAddSingleton(CreateIdentity);

		services.AddHttpClient(HttpClientNames.Default, static client => client.Timeout = TimeSpan.FromSeconds(15))
			.ConfigurePrimaryHttpMessageHandler(CreatePrimaryHandler)
			.SetHandlerLifetime(Timeout.InfiniteTimeSpan);

		services.AddHttpClient(HttpClientNames.BilibiliApi, static client => client.Timeout = TimeSpan.FromSeconds(15))
			.ConfigurePrimaryHttpMessageHandler(CreatePrimaryHandler)
			.AddHttpMessageHandler(CreateRequestHandler)
			.SetHandlerLifetime(Timeout.InfiniteTimeSpan);

		// 直播流持续数小时，超时由录制流程按连接和空闲分别控制
		services.AddHttpClient(HttpClientNames.BilibiliStream, static client => client.Timeout = Timeout.InfiniteTimeSpan)
			.ConfigurePrimaryHttpMessageHandler(CreatePrimaryHandler)
			.AddHttpMessageHandler(CreateRequestHandler)
			.SetHandlerLifetime(Timeout.InfiniteTimeSpan);

		services.TryAddSingleton(static provider => new BilibiliApiClient(provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientNames.BilibiliApi), provider.GetRequiredService<TimeProvider>()));
		services.TryAddSingleton(static provider => new DanmuClient(provider.GetRequiredService<BilibiliApiClient>(), provider.GetRequiredService<BilibiliIdentity>(), provider.GetRequiredService<ILogger<DanmuClient>>()));

		return services;
	}

	private static HttpMessageHandler CreatePrimaryHandler(IServiceProvider provider)
	{
		return new SocketsHttpHandler
		{
			Proxy = provider.GetRequiredService<SwitchableWebProxy>(),
			UseProxy = true,
			UseCookies = false,
			AutomaticDecompression = DecompressionMethods.All,
			PooledConnectionLifetime = TimeSpan.FromMinutes(10)
		};
	}

	// API 和直播流两条管道共用一个身份，B 站看到的是同一个 buvid3
	private static DelegatingHandler CreateRequestHandler(IServiceProvider provider)
	{
		return new BilibiliRequestHandler(provider.GetRequiredService<BilibiliIdentity>());
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
