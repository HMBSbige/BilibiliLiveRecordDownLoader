namespace BilibiliLiveRecordDownLoader.Application;

public static class ApplicationServiceCollectionExtensions
{
	public static IServiceCollection AddApplication(this IServiceCollection services)
	{
		services.TryAddSingleton(TimeProvider.System);

		services.AddBilibiliHttpClients();

		services.TryAddSingleton<IDiskSpaceService, DiskSpaceService>();
		services.TryAddSingleton<LowDiskSpaceMonitor>();
		services.TryAddSingleton<IUpdateAppService, UpdateAppService>();
		services.TryAddSingleton<IAccountAppService, AccountAppService>();
		services.TryAddSingleton<ISettingsAppService, SettingsAppService>();

		services.TryAddSingleton<FFmpegProvider>();
		services.TryAddSingleton<BackgroundTaskAppService>();
		services.TryAddSingleton<IBackgroundTaskAppService>(static provider => provider.GetRequiredService<BackgroundTaskAppService>());
		services.TryAddSingleton<IMediaToolAppService, MediaToolAppService>();

		services.TryAddSingleton<LiveRecorder>();
		services.TryAddSingleton<RecordingPostProcessor>();
		services.TryAddSingleton<RoomSessionFactory>();
		services.TryAddSingleton<RoomAppService>();
		services.TryAddSingleton<IRoomAppService>(static provider => provider.GetRequiredService<RoomAppService>());
		services.TryAddSingleton<IDanmakuAppService, DanmakuAppService>();

		services.TryAddSingleton<IAppLifecycleService, AppLifecycleService>();

		return services;
	}
}
