namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

public sealed partial class GeneralSettingsViewModel : ViewModelBase
{
	private static readonly TimeSpan DiskSpaceRefreshInterval = TimeSpan.FromSeconds(2);

	private bool _isInitialized;
	private bool _isRevertingAutoStart;

	public GeneralSettingsViewModel()
	{
		BrowseRecordDirectoryCommand.DisposeWith(Disposables);
		OpenRecordDirectoryCommand.DisposeWith(Disposables);
		BrowseFFmpegPathCommand.DisposeWith(Disposables);
	}

	public bool IsAutoStartSupported => AppLocator.Current.GetRequiredService<IAutoStartService>().IsSupported;

	[Reactive]
	public partial string RecordDirectory { get; set; } = string.Empty;

	[Reactive]
	public partial string? FFmpegPath { get; set; }

	[Reactive]
	public partial string? DiskSpaceText { get; private set; }

	[Reactive]
	public partial double DiskUsagePercent { get; private set; }

	[Reactive]
	public partial bool IsAutoStartEnabled { get; set; }

	internal void ApplyConfig(AppConfig config)
	{
		if (_isInitialized)
		{
			return;
		}

		RecordDirectory = config.RecordDirectory;
		FFmpegPath = config.FFmpegPath;

		PersistToConfig
		(
			this.WhenChanged(static viewModel => viewModel.RecordDirectory).Map(static directory => directory.Trim()),
			static (appConfig, value) => appConfig.RecordDirectory = string.IsNullOrEmpty(value) ? ConfigurationConsts.DefaultRecordDirectory : value
		);
		PersistToConfig(this.WhenChanged(static viewModel => viewModel.FFmpegPath), static (appConfig, value) => appConfig.FFmpegPath = string.IsNullOrWhiteSpace(value) ? null : value.Trim());

		IDiskSpaceService diskSpaceService = AppLocator.Current.GetRequiredService<IDiskSpaceService>();
		Poll(() => diskSpaceService.GetDiskSpace(RecordDirectory), ApplyDiskSpace, DiskSpaceRefreshInterval);

		IAutoStartService autoStartService = AppLocator.Current.GetRequiredService<IAutoStartService>();
		IsAutoStartEnabled = autoStartService.IsSupported && autoStartService.IsEnabled();

		this.WhenChanged(static viewModel => viewModel.IsAutoStartEnabled)
			.Skip(1)
			.Subscribe(enabled => ApplyAutoStart(autoStartService, enabled))
			.DisposeWith(Disposables);

		_isInitialized = true;
	}

	[ReactiveCommand]
	private async Task BrowseRecordDirectoryAsync()
	{
		if (await AppLocator.Current.GetRequiredService<IStoragePickerService>().PickFolderAsync(RecordDirectory) is { } directory)
		{
			RecordDirectory = directory;
		}
	}

	[ReactiveCommand]
	private async Task OpenRecordDirectoryAsync()
	{
		await AppLocator.Current.GetRequiredService<ILauncherService>().OpenFolderAsync(RecordDirectory);
	}

	[ReactiveCommand]
	private async Task BrowseFFmpegPathAsync()
	{
		string? initialDirectory = string.IsNullOrWhiteSpace(FFmpegPath) ? null : Path.GetDirectoryName(FFmpegPath);

		if (await AppLocator.Current.GetRequiredService<IStoragePickerService>().PickFileAsync(initialDirectory) is { } path)
		{
			FFmpegPath = path;
		}
	}

	private void ApplyAutoStart(IAutoStartService autoStartService, bool enabled)
	{
		if (_isRevertingAutoStart)
		{
			return;
		}

		try
		{
			autoStartService.SetEnabled(enabled);
		}
		catch (Exception exception)
		{
			RxState.DefaultExceptionHandler.OnNext(exception);

			// 开关要反映实际状态
			_isRevertingAutoStart = true;

			try
			{
				IsAutoStartEnabled = autoStartService.IsEnabled();
			}
			finally
			{
				_isRevertingAutoStart = false;
			}
		}
	}

	private void ApplyDiskSpace(DiskSpaceInfo? diskSpace)
	{
		DiskSpaceText = diskSpace is null ? null : AppLanguage.Current.DiskSpace.ToString(ByteSize.Format(diskSpace.AvailableBytes), ByteSize.Format(diskSpace.TotalBytes));
		DiskUsagePercent = (diskSpace?.UsedRatio ?? 0) * 100;
	}
}
