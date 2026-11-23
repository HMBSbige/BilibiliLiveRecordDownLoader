namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

public sealed class SettingsViewModel : ViewModelBase
{
	// 视图通过实例绑定访问这些属性
#pragma warning disable CA1822
	public GeneralSettingsViewModel General => AppLocator.Current.GetRequiredService<GeneralSettingsViewModel>();

	public RecordingSettingsViewModel Recording => AppLocator.Current.GetRequiredService<RecordingSettingsViewModel>();

	public DanmakuSettingsViewModel Danmaku => AppLocator.Current.GetRequiredService<DanmakuSettingsViewModel>();

	public NetworkSettingsViewModel Network => AppLocator.Current.GetRequiredService<NetworkSettingsViewModel>();

	public NotificationSettingsViewModel Notification => AppLocator.Current.GetRequiredService<NotificationSettingsViewModel>();

	public ExperimentalSettingsViewModel Experimental => AppLocator.Current.GetRequiredService<ExperimentalSettingsViewModel>();

	public AppearanceSettingsViewModel Appearance => AppLocator.Current.GetRequiredService<AppearanceSettingsViewModel>();

	public UpdateSettingsViewModel Update => AppLocator.Current.GetRequiredService<UpdateSettingsViewModel>();
#pragma warning restore CA1822

	public void ApplySettings(AppSettingsDto settings)
	{
		Appearance.ApplySettings(settings);
		General.ApplySettings(settings);
		Recording.ApplySettings(settings);
		Danmaku.ApplySettings(settings);
		Network.ApplySettings(settings);
		Notification.ApplySettings(settings);
		Experimental.ApplySettings(settings);
		Update.ApplySettings(settings);
	}
}
