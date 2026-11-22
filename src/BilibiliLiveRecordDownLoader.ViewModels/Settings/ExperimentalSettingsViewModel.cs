namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

public sealed partial class ExperimentalSettingsViewModel(IAppConfigManager configManager) : ViewModelBase
{
	private bool _isInitialized;

	[Reactive]
	public partial bool RemoveFillerData { get; set; }

	internal void ApplyConfig(AppConfig config)
	{
		if (_isInitialized)
		{
			return;
		}

		RemoveFillerData = config.RemoveFillerData;

		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.RemoveFillerData), static (appConfig, value) => appConfig.RemoveFillerData = value);

		_isInitialized = true;
	}
}
