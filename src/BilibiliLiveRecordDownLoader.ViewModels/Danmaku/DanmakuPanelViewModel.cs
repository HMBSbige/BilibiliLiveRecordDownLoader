namespace BilibiliLiveRecordDownLoader.ViewModels.Danmaku;

// 房间页右侧的弹幕面板：平时跟随选中的直播间显示实时弹幕，打开文件后显示文件，关掉文件回到实时弹幕
public sealed partial class DanmakuPanelViewModel : ViewModelBase
{
	private readonly IDanmakuAppService _danmaku;
	private readonly IAppConfigManager _configManager;
	private readonly Func<CancellationToken, Task<bool>> _showLogin;

	public DanmakuPanelViewModel(IDanmakuAppService danmaku, IAppConfigManager configManager, Func<CancellationToken, Task<bool>> showLogin)
	{
		_danmaku = danmaku;
		_configManager = configManager;
		_showLogin = showLogin;

		this.WhenChanged(static viewModel => viewModel.IsOpen)
			.Skip(1)
			.Subscribe(_ => Update())
			.DisposeWith(Disposables);

		CloseCommand.DisposeWith(Disposables);
		CloseFileCommand.DisposeWith(Disposables);
		OpenFileCommand.DisposeWith(Disposables);
		LoginCommand.DisposeWith(Disposables);
	}

	// 测试里关掉定时刷新，由测试调用 Refresh
	internal bool AutoRefresh { get; init; } = true;

	[Reactive]
	public partial bool IsOpen { get; set; }

	// 面板跟随的直播间；没有选过或者已被移除时为 null
	[Reactive]
	public partial RoomItemViewModel? Room { get; private set; }

	// 面板打开、没有打开文件且有直播间时才订阅
	[Reactive]
	public partial LiveDanmakuViewModel? Live { get; private set; }

	[Reactive]
	public partial DanmakuFileViewModel? File { get; private set; }

	[Reactive]
	public partial bool IsLoadingFile { get; private set; }

	// 打开文件失败的原因
	[Reactive]
	public partial string? ErrorMessage { get; private set; }

	// 只在选中单个直播间时调用；多选或取消选中时面板保持原来的直播间，免得来回切换
	internal void Follow(RoomItemViewModel room)
	{
		if (Room == room)
		{
			return;
		}

		Room = room;
		ErrorMessage = null;
		Update();
	}

	internal void OnRoomRemoved(RoomItemViewModel room)
	{
		if (Room != room)
		{
			return;
		}

		Room = null;
		Update();
	}

	internal async Task LoadFileAsync(string path, CancellationToken cancellationToken)
	{
		ErrorMessage = null;
		IsLoadingFile = true;

		try
		{
			DanmakuFileDto file = await _danmaku.OpenFileAsync(path, cancellationToken);
			File?.Dispose();
			File = new DanmakuFileViewModel(file, _danmaku);
		}
		catch (Exception exception) when (DanmakuFileViewModel.IsExpectedFailure(exception))
		{
			ErrorMessage = exception is FileNotFoundException ? AppLanguage.Current.FileNotFound.ToString(path) : exception.Message;
		}
		finally
		{
			IsLoadingFile = false;
		}

		IsOpen = true;
		Update();
	}

	// 默认打开跟随的直播间的目录，已经打开文件时打开文件所在的目录
	internal async Task PickFileAsync(CancellationToken cancellationToken)
	{
		string directory;

		if (File is { } file)
		{
			directory = Path.GetDirectoryName(file.Path) ?? string.Empty;
		}
		else
		{
			AppConfig config = await _configManager.GetAsync(cancellationToken);
			directory = Room is { } room ? RecordingFileLayout.GetRoomDirectory(config.RecordDirectory, room.RoomId) : config.RecordDirectory;
		}

		if (await AppLocator.Current.GetRequiredService<IStoragePickerService>().PickDanmakuFileAsync(directory) is { } path)
		{
			await LoadFileAsync(path, cancellationToken);
		}
	}

	[ReactiveCommand]
	private Task OpenFileAsync(CancellationToken cancellationToken)
	{
		return PickFileAsync(cancellationToken);
	}

	[ReactiveCommand]
	private void CloseFile()
	{
		File?.Dispose();
		File = null;
		ErrorMessage = null;
		Update();
	}

	[ReactiveCommand]
	private void Close()
	{
		IsOpen = false;
	}

	[ReactiveCommand]
	private async Task LoginAsync(CancellationToken cancellationToken)
	{
		await _showLogin(cancellationToken);
	}

	private void Update()
	{
		if (!IsOpen)
		{
			File?.Dispose();
			File = null;
			ErrorMessage = null;
		}

		long? roomId = IsOpen && File is null ? Room?.RoomId : null;

		if (Live?.RoomId == roomId)
		{
			return;
		}

		Live?.Dispose();
		Live = null;

		if (roomId is not { } id)
		{
			return;
		}

		try
		{
			Live = new LiveDanmakuViewModel(_danmaku.Subscribe(id), AutoRefresh);
		}
		catch (KeyNotFoundException)
		{
			// 房间刚被移除，列表下次同步时也会去掉它
			Room = null;
		}
	}

	public override void Dispose()
	{
		Live?.Dispose();
		File?.Dispose();
		base.Dispose();
	}
}
