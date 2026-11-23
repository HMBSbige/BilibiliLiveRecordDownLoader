using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.ViewModels.Settings;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class NotificationSettingsViewModelTest
{
	[Test]
	public async Task SwitchesAreRestoredFromAndSavedToTheConfigAsync(CancellationToken cancellationToken)
	{
		AppConfig config = AppConfig.CreateDefault();
		config.NotifyOnRecordingError = false;
		config.NotifyOnTitleChange = true;
		FakeConfigManager configManager = new(config);
		using NotificationSettingsViewModel viewModel = new(new SettingsAppService(configManager));

		viewModel.ApplySettings(config.ToSettingsDto());
		(bool recordingError, bool titleChange, int updates) = (viewModel.NotifyOnRecordingError, viewModel.NotifyOnTitleChange, configManager.UpdateCount);
		viewModel.NotifyOnRecordingError = true;
		viewModel.NotifyOnTitleChange = false;

		await TestWait.UntilAsync(() => configManager.UpdateCount >= 2, "both changes are saved", cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(recordingError).IsFalse();
			await Assert.That(titleChange).IsTrue();
			await Assert.That(updates).IsEqualTo(0);
			await Assert.That(config.NotifyOnRecordingError).IsTrue();
			await Assert.That(config.NotifyOnTitleChange).IsFalse();
		}
	}
}
