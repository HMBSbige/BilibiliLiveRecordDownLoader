namespace BilibiliLiveRecordDownLoader.ViewModels;

public static class ViewModelsServiceCollectionExtensions
{
	public static IServiceCollection AddViewModels(this IServiceCollection services)
	{
		services.AddApplicationContracts();
		services.AddDomain();

		services.TryAddSingleton<DialogHostViewModel>();
		services.TryAddSingleton<MainWindowViewModel>();
		services.TryAddSingleton<RoomListViewModel>();
		services.TryAddSingleton<TaskListViewModel>();
		services.TryAddSingleton<MediaToolsViewModel>();
		services.TryAddSingleton<LogViewModel>();
		services.TryAddSingleton<AccountViewModel>();
		services.TryAddSingleton<SettingsViewModel>();
		services.TryAddSingleton<GeneralSettingsViewModel>();
		services.TryAddSingleton<RecordingSettingsViewModel>();
		services.TryAddSingleton<DanmakuSettingsViewModel>();
		services.TryAddSingleton<NetworkSettingsViewModel>();
		services.TryAddSingleton<NotificationSettingsViewModel>();
		services.TryAddSingleton<ExperimentalSettingsViewModel>();
		services.TryAddSingleton<AppearanceSettingsViewModel>();
		services.TryAddSingleton<UpdateSettingsViewModel>();

		return services;
	}
}
