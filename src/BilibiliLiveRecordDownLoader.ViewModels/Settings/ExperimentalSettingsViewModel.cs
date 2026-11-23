namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

public sealed partial class ExperimentalSettingsViewModel(ISettingsAppService settingsAppService) : ViewModelBase
{
	[Reactive]
	public partial bool RemoveFillerData { get; set; }

	[Reactive]
	public partial bool RequestTrueOriginal { get; set; }

	[Reactive]
	public partial bool UseMasterPlaylistWhenRestricted { get; set; }

	internal void ApplySettings(AppSettingsDto settings)
	{
		RemoveFillerData = settings.RemoveFillerData;
		RequestTrueOriginal = settings.RequestTrueOriginal;
		UseMasterPlaylistWhenRestricted = settings.UseMasterPlaylistWhenRestricted;

		PersistSetting(settingsAppService, this.WhenChanged(static viewModel => viewModel.RemoveFillerData), static (current, value) => current with { RemoveFillerData = value });
		PersistSetting(settingsAppService, this.WhenChanged(static viewModel => viewModel.RequestTrueOriginal), static (current, value) => current with { RequestTrueOriginal = value });
		PersistSetting(settingsAppService, this.WhenChanged(static viewModel => viewModel.UseMasterPlaylistWhenRestricted), static (current, value) => current with { UseMasterPlaylistWhenRestricted = value });
	}
}
