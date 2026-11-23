namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

public sealed partial class NotificationSettingsViewModel(IAppConfigManager configManager) : ViewModelBase
{
	[Reactive]
	public partial bool NotifyOnRecordingError { get; set; }

	[Reactive]
	public partial bool NotifyOnTitleChange { get; set; }

	internal void ApplyConfig(AppConfig config)
	{
		NotifyOnRecordingError = config.NotifyOnRecordingError;
		NotifyOnTitleChange = config.NotifyOnTitleChange;

		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.NotifyOnRecordingError), static (appConfig, value) => appConfig.NotifyOnRecordingError = value);
		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.NotifyOnTitleChange), static (appConfig, value) => appConfig.NotifyOnTitleChange = value);
	}
}
