namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

public sealed partial class ExperimentalSettingsViewModel(IAppConfigManager configManager) : ViewModelBase
{
	private bool _isInitialized;

	[Reactive]
	public partial bool RemoveFillerData { get; set; }

	[Reactive]
	public partial bool RequestTrueOriginal { get; set; }

	[Reactive]
	public partial bool UseMasterPlaylistWhenRestricted { get; set; }

	internal void ApplyConfig(AppConfig config)
	{
		if (_isInitialized)
		{
			return;
		}

		RemoveFillerData = config.RemoveFillerData;
		RequestTrueOriginal = config.RequestTrueOriginal;
		UseMasterPlaylistWhenRestricted = config.UseMasterPlaylistWhenRestricted;

		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.RemoveFillerData), static (appConfig, value) => appConfig.RemoveFillerData = value);
		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.RequestTrueOriginal), static (appConfig, value) => appConfig.RequestTrueOriginal = value);
		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.UseMasterPlaylistWhenRestricted), static (appConfig, value) => appConfig.UseMasterPlaylistWhenRestricted = value);

		_isInitialized = true;
	}
}
