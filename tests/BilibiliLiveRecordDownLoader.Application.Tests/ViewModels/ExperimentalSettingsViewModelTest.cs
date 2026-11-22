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
			// 读取配置时不回写
			await Assert.That(updates).IsEqualTo(0);
			await Assert.That(config.RemoveFillerData).IsFalse();
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task TryingTheOriginalIsRestoredFromAndSavedToTheConfigAsync(CancellationToken cancellationToken)
	{
		AppConfig config = AppConfig.CreateDefault();
		config.TryOriginalWhenRestricted = true;
		FakeConfigManager configManager = new(config);
		using ExperimentalSettingsViewModel viewModel = new(configManager);

		viewModel.ApplyConfig(config);
		(bool restored, int updates) = (viewModel.TryOriginalWhenRestricted, configManager.UpdateCount);
		viewModel.TryOriginalWhenRestricted = false;

		while (configManager.UpdateCount < 1)
		{
			await Task.Delay(10, cancellationToken);
		}

		using (Assert.Multiple())
		{
			await Assert.That(restored).IsTrue();
			// 读取配置时不回写
			await Assert.That(updates).IsEqualTo(0);
			await Assert.That(config.TryOriginalWhenRestricted).IsFalse();
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
			// 读取配置时不回写
			await Assert.That(updates).IsEqualTo(0);
			await Assert.That(config.RequestTrueOriginal).IsFalse();
		}
	}
}
