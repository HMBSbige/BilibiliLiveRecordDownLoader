namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

public sealed partial class NetworkSettingsViewModel(ISettingsAppService settingsAppService) : ViewModelBase
{
	[Reactive]
	public partial string? UserAgent { get; set; }

	[Reactive]
	public partial bool UseSystemProxy { get; set; }

	internal void ApplySettings(AppSettingsDto settings)
	{
		UserAgent = settings.UserAgent;
		UseSystemProxy = settings.UseSystemProxy;

		PersistSetting(settingsAppService, this.WhenChanged(static viewModel => viewModel.UserAgent), static (current, value) => current with { UserAgent = value });
		PersistSetting(settingsAppService, this.WhenChanged(static viewModel => viewModel.UseSystemProxy), static (current, value) => current with { UseSystemProxy = value });
	}
}
