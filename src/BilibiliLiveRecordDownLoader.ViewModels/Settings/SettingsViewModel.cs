namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

public sealed partial class SettingsViewModel : ViewModelBase
{
	public GeneralSettingsViewModel General => AppLocator.Current.GetRequiredService<GeneralSettingsViewModel>();

	public RecordingSettingsViewModel Recording => AppLocator.Current.GetRequiredService<RecordingSettingsViewModel>();

	public NetworkSettingsViewModel Network => AppLocator.Current.GetRequiredService<NetworkSettingsViewModel>();

	public NotificationSettingsViewModel Notification => AppLocator.Current.GetRequiredService<NotificationSettingsViewModel>();

	public ExperimentalSettingsViewModel Experimental => AppLocator.Current.GetRequiredService<ExperimentalSettingsViewModel>();

	public AppearanceSettingsViewModel Appearance => AppLocator.Current.GetRequiredService<AppearanceSettingsViewModel>();

	public UpdateSettingsViewModel Update => AppLocator.Current.GetRequiredService<UpdateSettingsViewModel>();

	private bool _isInitialized;

	public void ApplyConfig(AppConfig config)
	{
		if (_isInitialized)
		{
			return;
		}

		Appearance.ApplyConfig(config);
		General.ApplyConfig(config);
		Recording.ApplyConfig(config);
		Network.ApplyConfig(config);
		Notification.ApplyConfig(config);
		Experimental.ApplyConfig(config);
		Update.ApplyConfig(config);

		_isInitialized = true;
	}
}
