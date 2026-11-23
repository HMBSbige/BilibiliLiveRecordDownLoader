using BilibiliLiveRecordDownLoader.Application;
using BilibiliLiveRecordDownLoader.Configuration;

namespace BilibiliLiveRecordDownLoader.Views;

public static class ViewsServiceCollectionExtensions
{
	private const string OutputTemplate = $"[{{Timestamp:O}}] [{{Level:u3}}] [{{{LogProperties.RoomId}}}] {{Message:lj}}{{NewLine}}{{Exception}}";

	public static IServiceCollection AddViews(this IServiceCollection services, AppStartupOptions startupOptions)
	{
		services.AddAppConfiguration();
		services.AddApplication();
		services.AddViewModels();

		ConfigureLogging(services);

		services.TryAddSingleton(startupOptions);
		services.TryAddSingleton<ApplicationShell>();
		services.TryAddSingleton<IApplicationShell>(static provider => provider.GetRequiredService<ApplicationShell>());
		services.TryAddSingleton<INotificationService, NotificationService>();
		services.TryAddSingleton<ILauncherService, LauncherService>();
		services.TryAddSingleton<IStoragePickerService, StoragePickerService>();
		services.TryAddSingleton<IThemeService, ThemeService>();
		services.TryAddSingleton<IAutoStartService, AutoStartService>();
		services.TryAddSingleton<NotificationExceptionHandler>();
		services.TryAddSingleton<GlobalExceptionHandler>();

		return services;
	}

	private static void ConfigureLogging(IServiceCollection services)
	{
#if DEBUG
		SelfLog.Enable(static message => Debug.Print(message));
#endif

		LogStoreSink logStoreSink = new();

		Log.Logger = new LoggerConfiguration()
#if DEBUG
			.MinimumLevel.Debug()
#else
			.MinimumLevel.Information()
#endif
			.MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
			.MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
			.Enrich.FromLogContext()
#if DEBUG
			.WriteTo.Async(static sinks => sinks.Debug(outputTemplate: OutputTemplate, formatProvider: CultureInfo.InvariantCulture))
#endif
			.WriteTo.Async
			(static sinks => sinks.File
				(
					Path.Combine(ConfigurationConsts.LogDirectory, $"{AppConsts.Name}.log"),
					outputTemplate: OutputTemplate,
					formatProvider: CultureInfo.InvariantCulture,
					fileSizeLimitBytes: 10 * 1024 * 1024,
					rollingInterval: RollingInterval.Day,
					rollOnFileSizeLimit: true,
					retainedFileCountLimit: 31
				)
			)
			.WriteTo.Sink(logStoreSink)
			.CreateLogger();

		AppLocator.CurrentMutable.UseSerilogFullLogger(Log.Logger);
		// 不传实例时容器释放走 Log.CloseAndFlush，与崩溃、关闭超时时的刷新合起来只释放一次
		services.AddLogging(static logging => logging.AddSerilog(dispose: true));
		services.TryAddSingleton<ILogStore>(logStoreSink);
	}
}
