namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

public sealed partial class UpdateSettingsViewModel : ViewModelBase
{
	private bool _isInitialized;

	public UpdateSettingsViewModel()
	{
		CheckUpdateCommand.DisposeWith(Disposables);
		OpenHomepageCommand.DisposeWith(Disposables);
		OpenConfigDirectoryCommand.DisposeWith(Disposables);

		this.WhenChanged(static viewModel => viewModel.LatestVersion)
			.Subscribe(_ => UpdateVersionText())
			.DisposeWith(Disposables);
		OnLanguageChanged(UpdateVersionText);
	}

	public string CurrentVersion => AppLocator.Current.GetRequiredService<IUpdateAppService>().CurrentVersion;

	[Reactive]
	public partial bool AutoCheckUpdate { get; set; }

	[Reactive]
	public partial bool IncludePreRelease { get; set; }

	[Reactive]
	public partial string? LatestVersion { get; private set; }

	[Reactive]
	public partial string VersionText { get; private set; } = string.Empty;

	internal void ApplyConfig(AppConfig config)
	{
		if (_isInitialized)
		{
			return;
		}

		AutoCheckUpdate = config.AutoCheckUpdate;
		IncludePreRelease = config.IncludePreRelease;

		PersistToConfig(this.WhenChanged(static viewModel => viewModel.AutoCheckUpdate), static (appConfig, value) => appConfig.AutoCheckUpdate = value);
		PersistToConfig(this.WhenChanged(static viewModel => viewModel.IncludePreRelease), static (appConfig, value) => appConfig.IncludePreRelease = value);

		_isInitialized = true;
	}

	internal async Task CheckForUpdateOnStartupAsync(AppConfig config, CancellationToken cancellationToken = default)
	{
		if (!config.AutoCheckUpdate)
		{
			return;
		}

		// 开机自启时网络可能还没就绪，失败不打扰用户
		if (await AppLocator.Current.GetRequiredService<IUpdateAppService>().TryCheckForUpdateAsync(IncludePreRelease, cancellationToken) is { } result)
		{
			ShowResult(result, silentOnLatest: true);
		}
	}

	[ReactiveCommand]
	private async Task CheckUpdateAsync(CancellationToken cancellationToken)
	{
		ShowResult(await AppLocator.Current.GetRequiredService<IUpdateAppService>().CheckForUpdateAsync(IncludePreRelease, cancellationToken), silentOnLatest: false);
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
