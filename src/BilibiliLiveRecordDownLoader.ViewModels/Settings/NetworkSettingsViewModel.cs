namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

public sealed partial class NetworkSettingsViewModel(IAppConfigManager configManager) : ViewModelBase
{
	[Reactive]
	public partial string? UserAgent { get; set; }

	[Reactive]
	public partial bool UseSystemProxy { get; set; }

	internal void ApplyConfig(AppConfig config)
	{
		UserAgent = config.UserAgent;
		UseSystemProxy = config.UseSystemProxy;

		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.UserAgent), static (appConfig, value) => appConfig.UserAgent = value);
		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.UseSystemProxy), static (appConfig, value) => appConfig.UseSystemProxy = value);
	}
}
