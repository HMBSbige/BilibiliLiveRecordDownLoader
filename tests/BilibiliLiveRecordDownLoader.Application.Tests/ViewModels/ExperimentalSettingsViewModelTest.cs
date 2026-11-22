using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.ViewModels.Settings;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class ExperimentalSettingsViewModelTest
{
	[Test]
	[Timeout(30_000)]
	public async Task FillerRemovalIsRestoredFromAndSavedToTheConfigAsync(CancellationToken cancellationToken)
	{
		AppConfig config = AppConfig.CreateDefault();
		config.RemoveFillerData = true;
		FakeConfigManager configManager = new(config);
		using ExperimentalSettingsViewModel viewModel = new(configManager);

		viewModel.ApplyConfig(config);
		(bool restored, int updates) = (viewModel.RemoveFillerData, configManager.UpdateCount);
		viewModel.RemoveFillerData = false;

		while (configManager.UpdateCount < 1)
		{
			await Task.Delay(10, cancellationToken);
		}

		using (Assert.Multiple())
		{
			await Assert.That(restored).IsTrue();
			await Assert.That(updates).IsEqualTo(0);
			await Assert.That(config.RemoveFillerData).IsFalse();
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task MasterPlaylistSwitchIsRestoredFromAndSavedToTheConfigAsync(CancellationToken cancellationToken)
	{
		// 默认开着，从关着的配置恢复才看得出读了配置
		AppConfig config = AppConfig.CreateDefault();
		config.UseMasterPlaylistWhenRestricted = false;
		FakeConfigManager configManager = new(config);
		using ExperimentalSettingsViewModel viewModel = new(configManager);

		viewModel.ApplyConfig(config);
		(bool restored, int updates) = (viewModel.UseMasterPlaylistWhenRestricted, configManager.UpdateCount);
		viewModel.UseMasterPlaylistWhenRestricted = true;

		while (configManager.UpdateCount < 1)
		{
			await Task.Delay(10, cancellationToken);
		}

		using (Assert.Multiple())
		{
			await Assert.That(restored).IsFalse();
			await Assert.That(updates).IsEqualTo(0);
			await Assert.That(config.UseMasterPlaylistWhenRestricted).IsTrue();
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task TrueOriginalRequestIsRestoredFromAndSavedToTheConfigAsync(CancellationToken cancellationToken)
	{
		AppConfig config = AppConfig.CreateDefault();
		config.RequestTrueOriginal = true;
		FakeConfigManager configManager = new(config);
		using ExperimentalSettingsViewModel viewModel = new(configManager);

		viewModel.ApplyConfig(config);
		(bool restored, int updates) = (viewModel.RequestTrueOriginal, configManager.UpdateCount);
		viewModel.RequestTrueOriginal = false;

		while (configManager.UpdateCount < 1)
		{
			await Task.Delay(10, cancellationToken);
		}

		using (Assert.Multiple())
		{
			await Assert.That(restored).IsTrue();
			await Assert.That(updates).IsEqualTo(0);
			await Assert.That(config.RequestTrueOriginal).IsFalse();
		}
	}
}
