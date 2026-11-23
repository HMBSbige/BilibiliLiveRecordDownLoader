namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

public sealed partial class GeneralSettingsViewModel : ViewModelBase
{
	private static readonly TimeSpan DiskSpaceRefreshInterval = TimeSpan.FromSeconds(2);

	private readonly ISettingsAppService _settingsAppService;
	private readonly IDiskSpaceService _diskSpaceService;
	private readonly IAutoStartService _autoStartService;
	private readonly IStoragePickerService _storagePicker;
	private readonly ILauncherService _launcher;
	private bool _isRevertingAutoStart;

	public GeneralSettingsViewModel(ISettingsAppService settingsAppService, IDiskSpaceService diskSpaceService, IAutoStartService autoStartService, IStoragePickerService storagePicker, ILauncherService launcher)
	{
		_settingsAppService = settingsAppService;
		_diskSpaceService = diskSpaceService;
		_autoStartService = autoStartService;
		_storagePicker = storagePicker;
		_launcher = launcher;

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

	internal void ApplySettings(AppSettingsDto settings)
	{
		RecordDirectory = settings.RecordDirectory;
		FFmpegPath = settings.FFmpegPath;

		// 输入框显示的就是实际保存的目录
		this.WhenChanged(static viewModel => viewModel.RecordDirectory)
			.Skip(1)
			.Subscribe(directory => RecordDirectory = ConfigValues.NormalizeRecordDirectory(directory))
			.DisposeWith(Disposables);

		PersistSetting(_settingsAppService, this.WhenChanged(static viewModel => viewModel.RecordDirectory), static (current, value) => current with { RecordDirectory = value });
		PersistSetting(_settingsAppService, this.WhenChanged(static viewModel => viewModel.FFmpegPath), static (current, value) => current with { FFmpegPath = value });

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
		if (await _storagePicker.PickFolderAsync(RecordDirectory) is { } directory)
		{
			RecordDirectory = directory;
		}
	}

	[ReactiveCommand]
	private async Task OpenRecordDirectoryAsync()
	{
		await _launcher.OpenFolderAsync(RecordDirectory);
	}

	[ReactiveCommand]
	private async Task BrowseFFmpegPathAsync()
	{
		string? initialDirectory = string.IsNullOrWhiteSpace(FFmpegPath) ? null : Path.GetDirectoryName(FFmpegPath);

		if (await _storagePicker.PickFileAsync(initialDirectory) is { } path)
		{
			FFmpegPath = path;
		}
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
