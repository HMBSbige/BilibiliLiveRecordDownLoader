namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

// 各分区都是容器里的单例，由容器释放
public sealed class SettingsViewModel(GeneralSettingsViewModel general, RecordingSettingsViewModel recording, DanmakuSettingsViewModel danmaku, NetworkSettingsViewModel network, NotificationSettingsViewModel notification, ExperimentalSettingsViewModel experimental, AppearanceSettingsViewModel appearance, UpdateSettingsViewModel update)
{
	public GeneralSettingsViewModel General { get; } = general;

	public RecordingSettingsViewModel Recording { get; } = recording;

	public DanmakuSettingsViewModel Danmaku { get; } = danmaku;

	public NetworkSettingsViewModel Network { get; } = network;

	public NotificationSettingsViewModel Notification { get; } = notification;

	public ExperimentalSettingsViewModel Experimental { get; } = experimental;

	public AppearanceSettingsViewModel Appearance { get; } = appearance;

	public UpdateSettingsViewModel Update { get; } = update;

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
