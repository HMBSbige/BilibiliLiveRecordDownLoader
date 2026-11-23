namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

public sealed partial class UpdateSettingsViewModel : ViewModelBase
{
	private readonly ISettingsAppService _settingsAppService;
	private readonly IUpdateAppService _updateAppService;

	public UpdateSettingsViewModel(ISettingsAppService settingsAppService, IUpdateAppService updateAppService)
	{
		_settingsAppService = settingsAppService;
		_updateAppService = updateAppService;

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

	internal async Task CheckForUpdateOnStartupAsync(CancellationToken cancellationToken = default)
	{
		if (!AutoCheckUpdate)
		{
			return;
		}

		// 开机自启时网络可能还没就绪，失败不打扰用户
		if (await _updateAppService.TryCheckForUpdateAsync(IncludePreRelease, cancellationToken) is { } result)
		{
			ShowResult(result, silentOnLatest: true);
		}
	}

	[ReactiveCommand]
	private async Task CheckUpdateAsync(CancellationToken cancellationToken)
	{
		ShowResult(await _updateAppService.CheckForUpdateAsync(IncludePreRelease, cancellationToken), silentOnLatest: false);
	}

	[ReactiveCommand]
	private async Task OpenHomepageAsync()
	{
		await AppLocator.Current.GetRequiredService<ILauncherService>().LaunchUriAsync(new Uri(AppConsts.HomepageUrl));
	}

	[ReactiveCommand]
	private async Task OpenConfigDirectoryAsync()
	{
		await AppLocator.Current.GetRequiredService<ILauncherService>().OpenFolderAsync(ConfigurationConsts.ConfigDirectory);
	}

	private void UpdateVersionText()
	{
		VersionText = LatestVersion is not { Length: > 0 } latest || latest == CurrentVersion
			? AppLanguage.Current.CurrentVersion.ToString(CurrentVersion)
			: AppLanguage.Current.CurrentAndLatestVersion.ToString(CurrentVersion, latest);
	}

	private void ShowResult(UpdateCheckResult result, bool silentOnLatest)
	{
		LatestVersion = result.LatestVersion;

		INotificationService notificationService = AppLocator.Current.GetRequiredService<INotificationService>();

		if (result is { HasUpdate: true, LatestVersion: { } latestVersion })
		{
			notificationService.Show(AppLanguage.Current.Update, AppLanguage.Current.NewVersionAvailable.ToString(latestVersion), link: new Uri(AppConsts.ReleasesUrl));
		}
		else if (!silentOnLatest)
		{
			notificationService.Show(AppLanguage.Current.Update, AppLanguage.Current.AlreadyLatestVersion, AppNotificationType.Success);
		}
	}
}
