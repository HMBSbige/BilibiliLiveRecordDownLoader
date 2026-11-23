namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

public sealed partial class DanmakuSettingsViewModel(ISettingsAppService settingsAppService) : ViewModelBase
{
	public IReadOnlyList<ChoiceOption> GiftModeOptions { get; } = ChoiceOption.Of(Enum.GetValues<DanmakuGiftMode>());

	[Reactive]
	public partial bool RecordSuperChats { get; set; }

	[Reactive]
	public partial DanmakuGiftMode GiftMode { get; set; }

	[Reactive]
	public partial bool RecordGuards { get; set; }

	[Reactive]
	public partial decimal OffsetSeconds { get; set; }

	[Reactive]
	public partial bool GenerateSubtitle { get; set; }

	[Reactive]
	public partial bool ShowPaidMessagesInSubtitle { get; set; }

	[Reactive]
	public partial bool SaveRawMessages { get; set; }

	[Reactive]
	public partial bool WriteChaptersOnConvert { get; set; }

	internal void ApplySettings(AppSettingsDto settings)
	{
		RecordSuperChats = settings.RecordSuperChats;
		GiftMode = settings.GiftMode;
		RecordGuards = settings.RecordGuards;
		OffsetSeconds = (decimal)settings.DanmakuOffset.TotalSeconds;
		GenerateSubtitle = settings.GenerateDanmakuSubtitle;
		ShowPaidMessagesInSubtitle = settings.ShowPaidMessagesInSubtitle;
		SaveRawMessages = settings.SaveRawDanmaku;
		WriteChaptersOnConvert = settings.WriteChaptersOnConvert;

		PersistSetting(settingsAppService, this.WhenChanged(static viewModel => viewModel.RecordSuperChats), static (current, value) => current with { RecordSuperChats = value });
		PersistSetting(settingsAppService, this.WhenChanged(static viewModel => viewModel.GiftMode), static (current, value) => current with { GiftMode = value });
		PersistSetting(settingsAppService, this.WhenChanged(static viewModel => viewModel.RecordGuards), static (current, value) => current with { RecordGuards = value });
		PersistSetting(settingsAppService, this.WhenChanged(static viewModel => viewModel.OffsetSeconds), static (current, value) => current with { DanmakuOffset = ConfigValues.DanmakuOffsetFromSeconds(value) }, RecordingSettingsViewModel.NumberInputQuietPeriod);
		PersistSetting(settingsAppService, this.WhenChanged(static viewModel => viewModel.GenerateSubtitle), static (current, value) => current with { GenerateDanmakuSubtitle = value });
		PersistSetting(settingsAppService, this.WhenChanged(static viewModel => viewModel.ShowPaidMessagesInSubtitle), static (current, value) => current with { ShowPaidMessagesInSubtitle = value });
		PersistSetting(settingsAppService, this.WhenChanged(static viewModel => viewModel.SaveRawMessages), static (current, value) => current with { SaveRawDanmaku = value });
		PersistSetting(settingsAppService, this.WhenChanged(static viewModel => viewModel.WriteChaptersOnConvert), static (current, value) => current with { WriteChaptersOnConvert = value });
	}
}
