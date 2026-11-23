namespace BilibiliLiveRecordDownLoader.Configuration;

public static class AppConfigurationServiceCollectionExtensions
{
	public static IServiceCollection AddAppConfiguration(this IServiceCollection services)
	{
		services.AddOptions<AppConfigStorageOptions>();
		services.TryAddSingleton(TimeProvider.System);
		services.TryAddSingleton<IAppConfigManager, AppConfigManager>();

		return services;
	}
}
