namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

public sealed partial class UpdateSettingsViewModel : ViewModelBase
{
	internal static readonly TimeSpan AutoCheckInterval = TimeSpan.FromDays(1);

	internal static readonly TimeSpan AutoCheckPollInterval = TimeSpan.FromHours(1);

	private readonly ISettingsAppService _settingsAppService;
	private readonly IUpdateAppService _updateAppService;
	private readonly INotificationService _notifications;
	private readonly ILauncherService _launcher;
	private readonly TimeProvider _timeProvider;
	private string? _notifiedVersion;

	public UpdateSettingsViewModel(ISettingsAppService settingsAppService, IUpdateAppService updateAppService, INotificationService notifications, ILauncherService launcher, TimeProvider timeProvider)
	{
		_settingsAppService = settingsAppService;
		_updateAppService = updateAppService;
		_notifications = notifications;
		_launcher = launcher;
		_timeProvider = timeProvider;

		CheckUpdateCommand.DisposeWith(Disposables);
		OpenHomepageCommand.DisposeWith(Disposables);
		OpenConfigDirectoryCommand.DisposeWith(Disposables);

		this.WhenChanged(static viewModel => viewModel.LatestVersion)
			.Subscribe(_ => UpdateVersionText())
			.DisposeWith(Disposables);
		OnLanguageChanged(UpdateVersionText);
	}

	private string CurrentVersion => _updateAppService.CurrentVersion;

	[Reactive]
	public partial bool AutoCheckUpdate { get; set; }

	[Reactive]
	public partial bool IncludePreRelease { get; set; }

	[Reactive]
	public partial string? LatestVersion { get; private set; }

	[Reactive]
	public partial string VersionText { get; private set; } = string.Empty;

	internal void ApplySettings(AppSettingsDto settings)
	{
		AutoCheckUpdate = settings.AutoCheckUpdate;
		IncludePreRelease = settings.IncludePreRelease;

		PersistSetting(_settingsAppService, this.WhenChanged(static viewModel => viewModel.AutoCheckUpdate), static (current, value) => current with { AutoCheckUpdate = value });
		PersistSetting(_settingsAppService, this.WhenChanged(static viewModel => viewModel.IncludePreRelease), static (current, value) => current with { IncludePreRelease = value });
	}

	internal void StartAutoCheck()
	{
		Forget(AutoCheckAsync);
	}

	private async Task AutoCheckAsync(CancellationToken cancellationToken)
	{
		using PeriodicTimer timer = new(AutoCheckPollInterval, _timeProvider);
		DateTimeOffset? lastCheck = null;

		do
		{
			DateTimeOffset now = _timeProvider.GetUtcNow();

			if (!AutoCheckUpdate || now - lastCheck < AutoCheckInterval)
			{
				continue;
			}

			lastCheck = now;

			if (await _updateAppService.TryCheckForUpdateAsync(IncludePreRelease, cancellationToken) is { } result)
			{
				ShowResult(result, isAutomatic: true);
			}
		}
		while (await timer.WaitForNextTickAsync(cancellationToken));
	}

	[ReactiveCommand]
	private async Task CheckUpdateAsync(CancellationToken cancellationToken)
	{
		ShowResult(await _updateAppService.CheckForUpdateAsync(IncludePreRelease, cancellationToken), isAutomatic: false);
	}

	[ReactiveCommand]
	private async Task OpenHomepageAsync()
	{
		await _launcher.LaunchUriAsync(new Uri(AppConsts.HomepageUrl));
	}

	[ReactiveCommand]
	private async Task OpenConfigDirectoryAsync()
	{
		await _launcher.OpenFolderAsync(ConfigurationConsts.ConfigDirectory);
	}

	private void UpdateVersionText()
	{
		VersionText = LatestVersion is not { Length: > 0 } latest || latest == CurrentVersion
			? AppLanguage.Current.CurrentVersion.ToString(CurrentVersion)
			: AppLanguage.Current.CurrentAndLatestVersion.ToString(CurrentVersion, latest);
	}

	private void ShowResult(UpdateCheckResult result, bool isAutomatic)
	{
		LatestVersion = result.LatestVersion;

		if (result is { HasUpdate: true, LatestVersion: { } latestVersion })
		{
			if (isAutomatic && latestVersion == _notifiedVersion)
			{
				return;
			}

			_notifiedVersion = latestVersion;
			_notifications.Show(AppLanguage.Current.Update, AppLanguage.Current.NewVersionAvailable.ToString(latestVersion), link: new Uri(AppConsts.ReleasesUrl));
		}
		else if (!isAutomatic)
		{
			_notifications.Show(AppLanguage.Current.Update, AppLanguage.Current.AlreadyLatestVersion, AppNotificationType.Success);
		}
	}
}
