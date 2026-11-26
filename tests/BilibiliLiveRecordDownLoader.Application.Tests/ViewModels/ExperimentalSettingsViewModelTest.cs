using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.ViewModels.Settings;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class ExperimentalSettingsViewModelTest
{
	[Test]
	public async Task RemoveFillerDataIsRestoredFromAndSavedToTheConfigAsync(CancellationToken cancellationToken)
	{
		AppConfig config = AppConfig.CreateDefault();
		bool configured = !config.RemoveFillerData;
		config.RemoveFillerData = configured;
		FakeConfigManager configManager = new(config);
		using ExperimentalSettingsViewModel viewModel = new(new SettingsAppService(configManager));

		viewModel.ApplySettings(config.ToSettingsDto());
		(bool restored, int updates) = (viewModel.RemoveFillerData, configManager.UpdateCount);
		viewModel.RemoveFillerData = !configured;

		await TestWait.UntilAsync(() => configManager.UpdateCount >= 1, "the change is saved", cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That((restored, updates)).IsEqualTo((configured, 0));
			await Assert.That(config.RemoveFillerData).IsEqualTo(!configured);
		}
	}
}
