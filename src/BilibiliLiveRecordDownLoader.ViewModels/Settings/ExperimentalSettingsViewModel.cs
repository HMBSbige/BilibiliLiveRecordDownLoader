namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

public sealed partial class ExperimentalSettingsViewModel(ISettingsAppService settingsAppService) : ViewModelBase
{
	[Reactive]
	public partial bool RemoveFillerData { get; set; }

	internal void ApplySettings(AppSettingsDto settings)
	{
		RemoveFillerData = settings.RemoveFillerData;

		PersistSetting(settingsAppService, this.WhenChanged(static viewModel => viewModel.RemoveFillerData), static (current, value) => current with { RemoveFillerData = value });
	}
}
