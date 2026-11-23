namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

public sealed partial class UpdateSettingsViewModel : ViewModelBase
{
	private readonly IAppConfigManager _configManager;
	private readonly IUpdateAppService _updateAppService;

	public UpdateSettingsViewModel(IAppConfigManager configManager, IUpdateAppService updateAppService)
	{
		_configManager = configManager;
		_updateAppService = updateAppService;

		CheckUpdateCommand.DisposeWith(Disposables);
		OpenHomepageCommand.DisposeWith(Disposables);
		OpenConfigDirectoryCommand.DisposeWith(Disposables);

		this.WhenChanged(static viewModel => viewModel.LatestVersion)
			.Subscribe(_ => UpdateVersionText())
			.DisposeWith(Disposables);
		OnLanguageChanged(UpdateVersionText);
	}

	public string CurrentVersion => _updateAppService.CurrentVersion;

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
		AutoCheckUpdate = config.AutoCheckUpdate;
		IncludePreRelease = config.IncludePreRelease;

		PersistToConfig(_configManager, this.WhenChanged(static viewModel => viewModel.AutoCheckUpdate), static (appConfig, value) => appConfig.AutoCheckUpdate = value);
		PersistToConfig(_configManager, this.WhenChanged(static viewModel => viewModel.IncludePreRelease), static (appConfig, value) => appConfig.IncludePreRelease = value);
	}

	internal async Task CheckForUpdateOnStartupAsync(AppConfig config, CancellationToken cancellationToken = default)
	{
		if (!config.AutoCheckUpdate)
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
