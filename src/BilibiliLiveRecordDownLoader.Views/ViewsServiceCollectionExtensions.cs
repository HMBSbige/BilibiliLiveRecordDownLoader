using BilibiliLiveRecordDownLoader.Application;
using BilibiliLiveRecordDownLoader.Configuration;

namespace BilibiliLiveRecordDownLoader.Views;

public static class ViewsServiceCollectionExtensions
{
	public static IServiceCollection AddViews(this IServiceCollection services, AppStartupOptions startupOptions)
	{
		services.AddAppConfiguration();
		services.AddApplication();
		services.AddViewModels();

		LogStoreSink logStore = AppLogging.EnsureConfigured();
		AppLocator.CurrentMutable.UseSerilogFullLogger(Log.Logger);
		// 不传实例时容器释放走 Log.CloseAndFlush，与崩溃、关闭超时时的刷新合起来只释放一次
		services.AddLogging(static logging => logging.AddSerilog(dispose: true));
		services.TryAddSingleton<ILogStore>(logStore);

		services.TryAddSingleton(startupOptions);
		services.TryAddSingleton<ApplicationShell>();
		services.TryAddSingleton<IApplicationShell>(static provider => provider.GetRequiredService<ApplicationShell>());
		services.TryAddSingleton<INotificationService, NotificationService>();
		services.TryAddSingleton<ILauncherService, LauncherService>();
		services.TryAddSingleton<IStoragePickerService, StoragePickerService>();
		services.TryAddSingleton<IThemeService, ThemeService>();
		services.TryAddSingleton<ISystemLanguageService, SystemLanguageService>();
		services.TryAddSingleton<IAutoStartService, AutoStartService>();
		services.TryAddSingleton<NotificationExceptionHandler>();
		services.TryAddSingleton<GlobalExceptionHandler>();

		return services;
	}
}
