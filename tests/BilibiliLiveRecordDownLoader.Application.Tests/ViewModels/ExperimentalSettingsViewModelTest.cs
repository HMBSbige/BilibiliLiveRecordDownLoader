using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.ViewModels.Settings;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class ExperimentalSettingsViewModelTest
{
	public enum Setting
	{
		RemoveFillerData
	}

	[Test]
	[Arguments(Setting.RemoveFillerData)]
	public async Task SettingIsRestoredFromAndSavedToTheConfigAsync(Setting setting, CancellationToken cancellationToken)
	{
		(Func<AppConfig, bool> readConfig, Action<AppConfig, bool> writeConfig, Func<ExperimentalSettingsViewModel, bool> readViewModel, Action<ExperimentalSettingsViewModel, bool> writeViewModel) = Accessors(setting);
		AppConfig config = AppConfig.CreateDefault();
		bool configured = !readConfig(config);
		writeConfig(config, configured);
		FakeConfigManager configManager = new(config);
		using ExperimentalSettingsViewModel viewModel = new(new SettingsAppService(configManager));

		viewModel.ApplySettings(config.ToSettingsDto());
		(bool restored, int updates) = (readViewModel(viewModel), configManager.UpdateCount);
		writeViewModel(viewModel, !configured);

		await TestWait.UntilAsync(() => configManager.UpdateCount >= 1, "the change is saved", cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(restored).IsEqualTo(configured);
			await Assert.That(updates).IsEqualTo(0);
			await Assert.That(readConfig(config)).IsEqualTo(!configured);
		}
	}

	private static (Func<AppConfig, bool> ReadConfig, Action<AppConfig, bool> WriteConfig, Func<ExperimentalSettingsViewModel, bool> ReadViewModel, Action<ExperimentalSettingsViewModel, bool> WriteViewModel) Accessors(Setting setting)
	{
		return setting switch
		{
			Setting.RemoveFillerData => (static config => config.RemoveFillerData, static (config, value) => config.RemoveFillerData = value, static viewModel => viewModel.RemoveFillerData, static (viewModel, value) => viewModel.RemoveFillerData = value)
		};
	}
}
