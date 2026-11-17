namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

public sealed partial class GeneralSettingsViewModel : ViewModelBase
{
	private static readonly TimeSpan DiskSpaceRefreshInterval = TimeSpan.FromSeconds(2);

	private bool _isInitialized;

	public GeneralSettingsViewModel()
	{
		BrowseRecordDirectoryCommand.DisposeWith(Disposables);
		OpenRecordDirectoryCommand.DisposeWith(Disposables);
		BrowseFFmpegPathCommand.DisposeWith(Disposables);
	}

	[Reactive]
	public partial string RecordDirectory { get; set; } = string.Empty;

	[Reactive]
	public partial string? FFmpegPath { get; set; }

	[Reactive]
	public partial string? DiskSpaceText { get; private set; }

	[Reactive]
	public partial double DiskUsagePercent { get; private set; }

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

	private void ApplyDiskSpace(DiskSpaceInfo? diskSpace)
	{
		DiskSpaceText = diskSpace is null ? null : AppLanguage.Current.DiskSpace.ToString(ByteSize.Format(diskSpace.AvailableBytes), ByteSize.Format(diskSpace.TotalBytes));
		DiskUsagePercent = (diskSpace?.UsedRatio ?? 0) * 100;
	}
}
