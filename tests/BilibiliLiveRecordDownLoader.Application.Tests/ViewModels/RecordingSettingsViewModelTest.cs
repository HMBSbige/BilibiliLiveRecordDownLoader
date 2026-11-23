using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.ViewModels.Settings;
using System.Windows.Input;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class RecordingSettingsViewModelTest
{
	[Test]
	public async Task SplitAndFileNameSettingsAreRestoredFromAndSavedToTheConfigAsync(CancellationToken cancellationToken)
	{
		AppConfig config = AppConfig.CreateDefault();
		config.SplitMode = RecordingSplitMode.Size;
		config.SplitDuration = TimeSpan.FromMinutes(30);
		config.SplitSizeMebibytes = 2000;
		config.SplitOnTitleChange = true;
		config.IncludeTitleInFileName = false;
		FakeConfigManager configManager = new(config);
		using RecordingSettingsViewModel viewModel = new(new SettingsAppService(configManager));

		viewModel.ApplySettings(config.ToSettingsDto());
		(RecordingSplitMode mode, decimal minutes, decimal mebibytes, bool onTitle, bool titleInName, bool isBySize, bool isByDuration) = (viewModel.SplitMode, viewModel.SplitDurationMinutes, viewModel.SplitSizeMebibytes, viewModel.SplitOnTitleChange, viewModel.IncludeTitleInFileName, viewModel.IsSplitBySize, viewModel.IsSplitByDuration);
		int updates = configManager.UpdateCount;
		viewModel.SplitMode = RecordingSplitMode.Duration;
		viewModel.SplitDurationMinutes = 90;
		viewModel.SplitSizeMebibytes = 512;
		viewModel.SplitOnTitleChange = false;
		viewModel.IncludeTitleInFileName = true;

		await TestWait.UntilAsync(() => configManager.UpdateCount >= 5, "all five changes are saved", cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(mode).IsEqualTo(RecordingSplitMode.Size);
			await Assert.That((minutes, mebibytes, onTitle, titleInName)).IsEqualTo((30m, 2000m, true, false));
			await Assert.That((isBySize, isByDuration)).IsEqualTo((true, false));
			await Assert.That(updates).IsEqualTo(0);
			await Assert.That((viewModel.IsSplitBySize, viewModel.IsSplitByDuration)).IsEqualTo((false, true));
			await Assert.That(config.SplitMode).IsEqualTo(RecordingSplitMode.Duration);
			await Assert.That(config.SplitDuration).IsEqualTo(TimeSpan.FromMinutes(90));
			await Assert.That(config.SplitSizeMebibytes).IsEqualTo(512);
			await Assert.That((config.SplitOnTitleChange, config.IncludeTitleInFileName)).IsEqualTo((false, true));
		}
	}

	[Test]
	public async Task NumberBeingTypedIsSavedOnceItStopsChangingAsync(CancellationToken cancellationToken)
	{
		AppConfig config = AppConfig.CreateDefault();
		config.SplitMode = RecordingSplitMode.Duration;
		config.SplitDuration = TimeSpan.FromMinutes(60);
		FakeConfigManager configManager = new(config);
		using RecordingSettingsViewModel viewModel = new(new SettingsAppService(configManager));
		viewModel.ApplySettings(config.ToSettingsDto());

		// 把 60 改成 90：先删成 6，再敲出 9 和 90
		viewModel.SplitDurationMinutes = 6;
		viewModel.SplitDurationMinutes = 9;
		viewModel.SplitDurationMinutes = 90;
		TimeSpan whileTyping = config.SplitDuration;

		await TestWait.UntilAsync(() => configManager.UpdateCount >= 1, "the change is saved", cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(whileTyping).IsEqualTo(TimeSpan.FromMinutes(60));
			await Assert.That(config.SplitDuration).IsEqualTo(TimeSpan.FromMinutes(90));
			await Assert.That(configManager.UpdateCount).IsEqualTo(1);
		}
	}

	// 保存可能排在别的写入后面，到时候界面上的列表可能又变了
	[Test]
	public async Task PriorityIsSavedAsItWasWhenItChangedAsync(CancellationToken cancellationToken)
	{
		AppConfig config = AppConfig.CreateDefault();
		GatedConfigManager configManager = new(config);
		using RecordingSettingsViewModel viewModel = new(new SettingsAppService(configManager));
		viewModel.ApplySettings(config.ToSettingsDto());

		((ICommand)viewModel.CodecPriority.MoveUpCommand).Execute(viewModel.CodecPriority.Items[1]);
		StreamCodec[] changed = [.. viewModel.CodecPriority.Values.OfType<StreamCodec>()];
		await configManager.Entered.Task.WaitAsync(cancellationToken);
		viewModel.CodecPriority.Items.Move(0, viewModel.CodecPriority.Items.Count - 1);
		configManager.Gate.SetResult();
		await configManager.Updated.Task.WaitAsync(cancellationToken);

		await Assert.That(config.CodecPriority).IsEquivalentTo(changed, CollectionOrdering.Matching);
	}

	[Test]
	public async Task NoValueIsShownWithoutSplittingAsync()
	{
		using RecordingSettingsViewModel viewModel = new(new SettingsAppService(new FakeConfigManager()));

		viewModel.ApplySettings(AppConfig.CreateDefault().ToSettingsDto());

		await Assert.That((viewModel.IsSplitBySize, viewModel.IsSplitByDuration)).IsEqualTo((false, false));
	}

	private sealed class GatedConfigManager(AppConfig config) : IAppConfigManager
	{
		public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public TaskCompletionSource Updated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public bool IsLegacyConfigImported => false;

		public ValueTask<AppConfig> GetAsync(CancellationToken cancellationToken = default)
		{
			return ValueTask.FromResult(config);
		}

		public async ValueTask UpdateAsync(Action<AppConfig> update, CancellationToken cancellationToken = default)
		{
			Entered.TrySetResult();
			await Gate.Task.WaitAsync(cancellationToken);
			update(config);
			Updated.TrySetResult();
		}
	}
}
