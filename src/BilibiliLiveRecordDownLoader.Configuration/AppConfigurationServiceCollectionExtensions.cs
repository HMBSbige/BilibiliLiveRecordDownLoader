namespace BilibiliLiveRecordDownLoader.Configuration;

public static class AppConfigurationServiceCollectionExtensions
{
	public static IServiceCollection AddAppConfiguration(this IServiceCollection services)
	{
		services.AddDomain();

		services.AddOptions<AppConfigStorageOptions>();
		services.TryAddSingleton<IAppConfigManager, AppConfigManager>();

		return services;
	}
}
