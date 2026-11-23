namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

public sealed partial class NotificationSettingsViewModel(ISettingsAppService settingsAppService) : ViewModelBase
{
	[Reactive]
	public partial bool NotifyOnRecordingError { get; set; }

	[Reactive]
	public partial bool NotifyOnTitleChange { get; set; }

	internal void ApplySettings(AppSettingsDto settings)
	{
		NotifyOnRecordingError = settings.NotifyOnRecordingError;
		NotifyOnTitleChange = settings.NotifyOnTitleChange;

		PersistSetting(settingsAppService, this.WhenChanged(static viewModel => viewModel.NotifyOnRecordingError), static (current, value) => current with { NotifyOnRecordingError = value });
		PersistSetting(settingsAppService, this.WhenChanged(static viewModel => viewModel.NotifyOnTitleChange), static (current, value) => current with { NotifyOnTitleChange = value });
	}
}
