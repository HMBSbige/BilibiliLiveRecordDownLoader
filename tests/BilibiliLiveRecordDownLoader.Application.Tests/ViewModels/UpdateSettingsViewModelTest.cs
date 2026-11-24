using BilibiliLiveRecordDownLoader.Application.Contracts.Updates;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using BilibiliLiveRecordDownLoader.ViewModels.Settings;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class UpdateSettingsViewModelTest
{
	[Test]
	public async Task AutomaticChecksRemindOnceForEachNewVersionAsync(CancellationToken cancellationToken)
	{
		FakeUpdateAppService updates = new();
		RecordingNotifications notifications = new();
		FakeTimeProvider time = new();
		using UpdateSettingsViewModel viewModel = Create(updates, time, notifications);

		viewModel.StartAutoCheck();
		time.Advance(UpdateSettingsViewModel.AutoCheckInterval);
		await TestWait.UntilAsync(() => updates.Checks == 2, "the second daily check runs", cancellationToken);
		updates.Latest = "4.2.0";
		time.Advance(UpdateSettingsViewModel.AutoCheckInterval);
		await TestWait.UntilAsync(() => notifications.Shown.Any(static shown => shown.Message == NewVersionMessage("4.2.0")), "the newer version is announced", cancellationToken);

		await Assert.That(notifications.Shown.Select(static shown => shown.Message)).IsEquivalentTo([NewVersionMessage("4.1.0"), NewVersionMessage("4.2.0")], CollectionOrdering.Matching);
	}

	[Test]
	public async Task TurningAutomaticChecksOnChecksAtTheNextPollAsync(CancellationToken cancellationToken)
	{
		FakeUpdateAppService updates = new();
		FakeTimeProvider time = new();
		using UpdateSettingsViewModel viewModel = Create(updates, time);
		viewModel.AutoCheckUpdate = false;

		viewModel.StartAutoCheck();
		int checksWhileOff = updates.Checks;
		viewModel.AutoCheckUpdate = true;
		time.Advance(UpdateSettingsViewModel.AutoCheckPollInterval);

		await TestWait.UntilAsync(() => updates.Checks == 1, "the switch is honoured at the next poll", cancellationToken);
		await Assert.That(checksWhileOff).IsEqualTo(0);
	}

	[Test]
	public async Task TimeSpentAsleepCountsTowardsTheNextCheckAsync(CancellationToken cancellationToken)
	{
		FakeUpdateAppService updates = new();
		FakeTimeProvider time = new();
		using UpdateSettingsViewModel viewModel = Create(updates, time);

		viewModel.StartAutoCheck();
		time.AdjustTime(time.GetUtcNow() + UpdateSettingsViewModel.AutoCheckInterval);
		time.Advance(UpdateSettingsViewModel.AutoCheckPollInterval);

		await TestWait.UntilAsync(() => updates.Checks == 2, "the check after waking up runs", cancellationToken);
	}

	private static UpdateSettingsViewModel Create(FakeUpdateAppService updates, FakeTimeProvider time, RecordingNotifications? notifications = null)
	{
		return new UpdateSettingsViewModel(new SettingsAppService(new FakeConfigManager()), updates, notifications ?? new RecordingNotifications(), new PlatformStub(), time) { AutoCheckUpdate = true };
	}

	private static string NewVersionMessage(string version)
	{
		return AppLanguage.Current.NewVersionAvailable.ToString(version);
	}

	private sealed class FakeUpdateAppService : IUpdateAppService
	{
		private int _checks;

		public string CurrentVersion => "4.0.0";

		public string Latest { get; set; } = "4.1.0";

		public int Checks => Volatile.Read(ref _checks);

		public Task<UpdateCheckResult> CheckForUpdateAsync(bool includePreRelease, CancellationToken cancellationToken = default)
		{
			UpdateCheckResult result = new(Latest != CurrentVersion, Latest);
			Interlocked.Increment(ref _checks);
			return Task.FromResult(result);
		}

		public async Task<UpdateCheckResult?> TryCheckForUpdateAsync(bool includePreRelease, CancellationToken cancellationToken = default)
		{
			return await CheckForUpdateAsync(includePreRelease, cancellationToken);
		}
	}
}
