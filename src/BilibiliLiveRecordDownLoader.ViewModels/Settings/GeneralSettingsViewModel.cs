namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

public sealed partial class GeneralSettingsViewModel : ViewModelBase
{
	private static readonly TimeSpan DiskSpaceRefreshInterval = TimeSpan.FromSeconds(2);

	private readonly IAppConfigManager _configManager;
	private readonly IDiskSpaceService _diskSpaceService;
	private readonly IAutoStartService _autoStartService;
	private bool _isRevertingAutoStart;

	public GeneralSettingsViewModel(IAppConfigManager configManager, IDiskSpaceService diskSpaceService, IAutoStartService autoStartService)
	{
		_configManager = configManager;
		_diskSpaceService = diskSpaceService;
		_autoStartService = autoStartService;

		BrowseRecordDirectoryCommand.DisposeWith(Disposables);
		OpenRecordDirectoryCommand.DisposeWith(Disposables);
		BrowseFFmpegPathCommand.DisposeWith(Disposables);
	}

	public bool IsAutoStartSupported => _autoStartService.IsSupported;

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
		RecordDirectory = config.RecordDirectory;
		FFmpegPath = config.FFmpegPath;

		// 输入框显示的就是实际保存的目录
		this.WhenChanged(static viewModel => viewModel.RecordDirectory)
			.Skip(1)
			.Subscribe(directory => RecordDirectory = NormalizeRecordDirectory(directory))
			.DisposeWith(Disposables);

		PersistToConfig(_configManager, this.WhenChanged(static viewModel => viewModel.RecordDirectory), static (appConfig, value) => appConfig.RecordDirectory = NormalizeRecordDirectory(value));
		PersistToConfig(_configManager, this.WhenChanged(static viewModel => viewModel.FFmpegPath), static (appConfig, value) => appConfig.FFmpegPath = string.IsNullOrWhiteSpace(value) ? null : value.Trim());

		Poll(() => _diskSpaceService.GetDiskSpace(RecordDirectory), ApplyDiskSpace, DiskSpaceRefreshInterval);

		IsAutoStartEnabled = _autoStartService.IsSupported && _autoStartService.IsEnabled();

		this.WhenChanged(static viewModel => viewModel.IsAutoStartEnabled)
			.Skip(1)
			.Subscribe(ApplyAutoStart)
			.DisposeWith(Disposables);
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

	private static string NormalizeRecordDirectory(string directory)
	{
		return directory.Trim() is { Length: > 0 } trimmed ? trimmed : ConfigurationConsts.DefaultRecordDirectory;
	}

	private void ApplyAutoStart(bool enabled)
	{
		if (_isRevertingAutoStart)
		{
			return;
		}

		try
		{
			_autoStartService.SetEnabled(enabled);
		}
		catch (Exception exception)
		{
			RxState.DefaultExceptionHandler.OnNext(exception);
			_isRevertingAutoStart = true;

			try
			{
				IsAutoStartEnabled = _autoStartService.IsEnabled();
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
