namespace BilibiliLiveRecordDownLoader.Application;

public static class ApplicationServiceCollectionExtensions
{
	public static IServiceCollection AddApplication(this IServiceCollection services)
	{
		services.AddApplicationContracts();
		services.AddDomain();

		services.AddBilibiliHttpClients();

		services.TryAddSingleton<ILogStore, LogStore>();
		services.TryAddSingleton<IDiskSpaceService, DiskSpaceService>();
		services.TryAddSingleton<IUpdateAppService, UpdateAppService>();
		services.TryAddSingleton<IAccountAppService, AccountAppService>();

		services.TryAddSingleton<FFmpegProvider>();
		services.TryAddSingleton<BackgroundTaskAppService>();
		services.TryAddSingleton<IBackgroundTaskAppService>(static provider => provider.GetRequiredService<BackgroundTaskAppService>());
		services.TryAddSingleton<IMediaToolAppService, MediaToolAppService>();

		services.TryAddSingleton<LiveRecorder>();
		services.TryAddSingleton<RecordingPostProcessor>();
		services.TryAddSingleton<RoomSessionFactory>();
		services.TryAddSingleton<RoomAppService>();
		services.TryAddSingleton<IRoomAppService>(static provider => provider.GetRequiredService<RoomAppService>());

		services.TryAddSingleton<IAppLifecycleService, AppLifecycleService>();

		return services;
	}
}
