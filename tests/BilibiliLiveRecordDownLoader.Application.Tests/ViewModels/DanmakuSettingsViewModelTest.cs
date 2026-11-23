using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.ViewModels.Settings;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class DanmakuSettingsViewModelTest
{
	[Test]
	public async Task SettingsAreRestoredFromAndSavedToTheConfigAsync(CancellationToken cancellationToken)
	{
		AppConfig config = AppConfig.CreateDefault();
		config.RecordSuperChats = false;
		config.GiftMode = DanmakuGiftMode.Paid;
		config.RecordGuards = false;
		config.DanmakuOffset = TimeSpan.FromSeconds(1.5);
		config.GenerateDanmakuSubtitle = false;
		config.ShowPaidMessagesInSubtitle = false;
		config.SaveRawDanmaku = true;
		config.WriteChaptersOnConvert = true;
		FakeConfigManager configManager = new(config);
		using DanmakuSettingsViewModel viewModel = new(new SettingsAppService(configManager));

		viewModel.ApplySettings(config.ToSettingsDto());
		(bool, DanmakuGiftMode, bool, decimal, bool, bool, bool, bool) restored = (viewModel.RecordSuperChats, viewModel.GiftMode, viewModel.RecordGuards, viewModel.OffsetSeconds, viewModel.GenerateSubtitle, viewModel.ShowPaidMessagesInSubtitle, viewModel.SaveRawMessages, viewModel.WriteChaptersOnConvert);
		int updates = configManager.UpdateCount;

		viewModel.RecordSuperChats = true;
		viewModel.GiftMode = DanmakuGiftMode.All;
		viewModel.RecordGuards = true;
		viewModel.OffsetSeconds = -0.8m;
		viewModel.GenerateSubtitle = true;
		viewModel.ShowPaidMessagesInSubtitle = true;
		viewModel.SaveRawMessages = false;
		viewModel.WriteChaptersOnConvert = false;

		await TestWait.UntilAsync(() => configManager.UpdateCount >= 8, "all eight changes are saved", cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(restored).IsEqualTo((false, DanmakuGiftMode.Paid, false, 1.5m, false, false, true, true));
			await Assert.That(updates).IsEqualTo(0);
			await Assert.That((config.RecordSuperChats, config.GiftMode, config.RecordGuards)).IsEqualTo((true, DanmakuGiftMode.All, true));
			await Assert.That(config.DanmakuOffset).IsEqualTo(TimeSpan.FromMilliseconds(-800));
			await Assert.That((config.GenerateDanmakuSubtitle, config.ShowPaidMessagesInSubtitle, config.SaveRawDanmaku, config.WriteChaptersOnConvert)).IsEqualTo((true, true, false, false));
		}
	}
}
