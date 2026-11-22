using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.ViewModels.Common;
using BilibiliLiveRecordDownLoader.ViewModels.Settings;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class RecordingSettingsViewModelTest
{
	[Test]
	[Timeout(30_000)]
	public async Task SplitSettingsAreRestoredFromAndSavedToTheConfigAsync(CancellationToken cancellationToken)
	{
		AppConfig config = AppConfig.CreateDefault();
		config.SplitMode = RecordingSplitMode.Size;
		config.SplitDuration = TimeSpan.FromMinutes(30);
		config.SplitSizeMegabytes = 2000;
		config.SplitOnTitleChange = true;
		FakeConfigManager configManager = new(config);
		using RecordingSettingsViewModel viewModel = new(configManager);

		viewModel.ApplyConfig(config);
		(ChoiceOption mode, decimal minutes, decimal megabytes, bool onTitle, bool isBySize, bool isByDuration) = (viewModel.SplitMode, viewModel.SplitDurationMinutes, viewModel.SplitSizeMegabytes, viewModel.SplitOnTitleChange, viewModel.IsSplitBySize, viewModel.IsSplitByDuration);
		int updates = configManager.UpdateCount;
		viewModel.SplitMode = new ChoiceOption(RecordingSplitMode.Duration);
		viewModel.SplitDurationMinutes = 90;
		viewModel.SplitSizeMegabytes = 512;
		viewModel.SplitOnTitleChange = false;

		while (configManager.UpdateCount < 4)
		{
			await Task.Delay(10, cancellationToken);
		}

		using (Assert.Multiple())
		{
			await Assert.That(mode).IsEqualTo(new ChoiceOption(RecordingSplitMode.Size));
			await Assert.That((minutes, megabytes, onTitle)).IsEqualTo((30m, 2000m, true));
			await Assert.That((isBySize, isByDuration)).IsEqualTo((true, false));
			await Assert.That(updates).IsEqualTo(0);
			await Assert.That((viewModel.IsSplitBySize, viewModel.IsSplitByDuration)).IsEqualTo((false, true));
			await Assert.That(config.SplitMode).IsEqualTo(RecordingSplitMode.Duration);
			await Assert.That(config.SplitDuration).IsEqualTo(TimeSpan.FromMinutes(90));
			await Assert.That(config.SplitSizeMegabytes).IsEqualTo(512);
			await Assert.That(config.SplitOnTitleChange).IsFalse();
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task TitleInFileNameIsRestoredFromAndSavedToTheConfigAsync(CancellationToken cancellationToken)
	{
		AppConfig config = AppConfig.CreateDefault();
		config.IncludeTitleInFileName = false;
		FakeConfigManager configManager = new(config);
		using RecordingSettingsViewModel viewModel = new(configManager);

		viewModel.ApplyConfig(config);
		bool restored = viewModel.IncludeTitleInFileName;
		viewModel.IncludeTitleInFileName = true;

		while (configManager.UpdateCount < 1)
		{
			await Task.Delay(10, cancellationToken);
		}

		using (Assert.Multiple())
		{
			await Assert.That(restored).IsFalse();
			await Assert.That(config.IncludeTitleInFileName).IsTrue();
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task NumberBeingTypedIsSavedOnceItStopsChangingAsync(CancellationToken cancellationToken)
	{
		AppConfig config = AppConfig.CreateDefault();
		config.SplitMode = RecordingSplitMode.Duration;
		config.SplitDuration = TimeSpan.FromMinutes(60);
		FakeConfigManager configManager = new(config);
		using RecordingSettingsViewModel viewModel = new(configManager);
		viewModel.ApplyConfig(config);

		// 把 60 改成 90：先删成 6，再敲出 9 和 90
		viewModel.SplitDurationMinutes = 6;
		viewModel.SplitDurationMinutes = 9;
		viewModel.SplitDurationMinutes = 90;
		TimeSpan whileTyping = config.SplitDuration;

		while (configManager.UpdateCount < 1)
		{
			await Task.Delay(10, cancellationToken);
		}

		await Task.Delay(RecordingSettingsViewModel.NumberInputQuietPeriod, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(whileTyping).IsEqualTo(TimeSpan.FromMinutes(60));
			await Assert.That(config.SplitDuration).IsEqualTo(TimeSpan.FromMinutes(90));
			await Assert.That(configManager.UpdateCount).IsEqualTo(1);
		}
	}

	[Test]
	public async Task NoValueIsShownWithoutSplittingAsync()
	{
		using RecordingSettingsViewModel viewModel = new(new FakeConfigManager());

		viewModel.ApplyConfig(AppConfig.CreateDefault());

		await Assert.That((viewModel.IsSplitBySize, viewModel.IsSplitByDuration)).IsEqualTo((false, false));
	}
}
