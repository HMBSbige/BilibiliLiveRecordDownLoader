namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

public sealed partial class ExperimentalSettingsViewModel(ISettingsAppService settingsAppService) : ViewModelBase
{
	[Reactive]
	public partial bool RemoveFillerData { get; set; }

	[Reactive]
	public partial bool UseMasterPlaylistWhenRestricted { get; set; }

	internal void ApplySettings(AppSettingsDto settings)
	{
		RemoveFillerData = settings.RemoveFillerData;
		UseMasterPlaylistWhenRestricted = settings.UseMasterPlaylistWhenRestricted;

		PersistSetting(settingsAppService, this.WhenChanged(static viewModel => viewModel.RemoveFillerData), static (current, value) => current with { RemoveFillerData = value });
		PersistSetting(settingsAppService, this.WhenChanged(static viewModel => viewModel.UseMasterPlaylistWhenRestricted), static (current, value) => current with { UseMasterPlaylistWhenRestricted = value });
	}
}
