namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

public sealed partial class UpdateSettingsViewModel : ViewModelBase
{
	private bool _isInitialized;

	public UpdateSettingsViewModel()
	{
		CheckUpdateCommand.DisposeWith(Disposables);
		OpenHomepageCommand.DisposeWith(Disposables);
	}

	public string CurrentVersion => AppLocator.Current.GetRequiredService<IUpdateAppService>().CurrentVersion;

	[Reactive]
	public partial bool AutoCheckUpdate { get; set; }

	[Reactive]
	public partial bool IncludePreRelease { get; set; }

	[Reactive]
	public partial string? LatestVersion { get; private set; }

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

	internal Task CheckForUpdateOnStartupAsync(AppConfig config, CancellationToken cancellationToken = default)
	{
		return config.AutoCheckUpdate ? CheckForUpdateCoreAsync(true, cancellationToken) : Task.CompletedTask;
	}

	[ReactiveCommand]
	private async Task CheckUpdateAsync(CancellationToken cancellationToken)
	{
		await CheckForUpdateCoreAsync(false, cancellationToken);
	}

	[ReactiveCommand]
	private async Task OpenHomepageAsync()
	{
		await AppLocator.Current.GetRequiredService<ILauncherService>().LaunchUriAsync(new Uri(AppConsts.HomepageUrl));
	}

	private async Task CheckForUpdateCoreAsync(bool silentOnLatest, CancellationToken cancellationToken)
	{
		UpdateCheckResult result = await AppLocator.Current.GetRequiredService<IUpdateAppService>().CheckForUpdateAsync(IncludePreRelease, cancellationToken);
		LatestVersion = result.LatestVersion;

		INotificationService notificationService = AppLocator.Current.GetRequiredService<INotificationService>();

		if (result is { HasUpdate: true, LatestVersion: { } latestVersion })
		{
			notificationService.Show(AppLanguage.Current.Update, AppLanguage.Current.NewVersionAvailable.ToString(latestVersion));
		}
		else if (!silentOnLatest)
		{
			notificationService.Show(AppLanguage.Current.Update, AppLanguage.Current.AlreadyLatestVersion, AppNotificationType.Success);
		}
	}
}
