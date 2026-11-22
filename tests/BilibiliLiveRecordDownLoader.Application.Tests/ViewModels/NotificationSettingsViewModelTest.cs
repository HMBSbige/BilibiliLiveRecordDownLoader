using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.ViewModels.Settings;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class NotificationSettingsViewModelTest
{
	[Test]
	[Timeout(30_000)]
	public async Task SwitchesAreRestoredFromAndSavedToTheConfigAsync(CancellationToken cancellationToken)
	{
		AppConfig config = AppConfig.CreateDefault();
		config.NotifyOnRecordingError = false;
		config.NotifyOnTitleChange = true;
		FakeConfigManager configManager = new(config);
		using NotificationSettingsViewModel viewModel = new(configManager);

		viewModel.ApplyConfig(config);
		(bool recordingError, bool titleChange, int updates) = (viewModel.NotifyOnRecordingError, viewModel.NotifyOnTitleChange, configManager.UpdateCount);
		viewModel.NotifyOnRecordingError = true;
		viewModel.NotifyOnTitleChange = false;

		while (configManager.UpdateCount < 2)
		{
			await Task.Delay(10, cancellationToken);
		}

		using (Assert.Multiple())
		{
			await Assert.That(recordingError).IsFalse();
			await Assert.That(titleChange).IsTrue();
			// 读取配置时不回写
			await Assert.That(updates).IsEqualTo(0);
			await Assert.That(config.NotifyOnRecordingError).IsTrue();
			await Assert.That(config.NotifyOnTitleChange).IsFalse();
		}
	}
}
