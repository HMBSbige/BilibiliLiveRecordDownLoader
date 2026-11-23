namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

public sealed partial class DanmakuSettingsViewModel(IAppConfigManager configManager) : ViewModelBase
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

	internal void ApplyConfig(AppConfig config)
	{
		RecordSuperChats = config.RecordSuperChats;
		GiftMode = config.GiftMode;
		RecordGuards = config.RecordGuards;
		OffsetSeconds = (decimal)config.DanmakuOffset.TotalSeconds;
		GenerateSubtitle = config.GenerateDanmakuSubtitle;
		ShowPaidMessagesInSubtitle = config.ShowPaidMessagesInSubtitle;
		SaveRawMessages = config.SaveRawDanmaku;
		WriteChaptersOnConvert = config.WriteChaptersOnConvert;

		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.RecordSuperChats), static (appConfig, value) => appConfig.RecordSuperChats = value);
		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.GiftMode), static (appConfig, value) => appConfig.GiftMode = value);
		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.RecordGuards), static (appConfig, value) => appConfig.RecordGuards = value);
		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.OffsetSeconds), static (appConfig, value) => appConfig.DanmakuOffset = TimeSpan.FromSeconds((double)value), RecordingSettingsViewModel.NumberInputQuietPeriod);
		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.GenerateSubtitle), static (appConfig, value) => appConfig.GenerateDanmakuSubtitle = value);
		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.ShowPaidMessagesInSubtitle), static (appConfig, value) => appConfig.ShowPaidMessagesInSubtitle = value);
		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.SaveRawMessages), static (appConfig, value) => appConfig.SaveRawDanmaku = value);
		PersistToConfig(configManager, this.WhenChanged(static viewModel => viewModel.WriteChaptersOnConvert), static (appConfig, value) => appConfig.WriteChaptersOnConvert = value);
	}
}
