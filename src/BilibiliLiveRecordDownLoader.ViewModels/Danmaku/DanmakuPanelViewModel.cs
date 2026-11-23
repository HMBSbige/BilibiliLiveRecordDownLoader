using System.Diagnostics.CodeAnalysis;

namespace BilibiliLiveRecordDownLoader.ViewModels.Danmaku;

public sealed partial class DanmakuPanelViewModel : ViewModelBase
{
	private readonly IDanmakuAppService _danmaku;
	private readonly IRoomAppService _roomAppService;
	private readonly ISettingsAppService _settingsAppService;
	private readonly IStoragePickerService _storagePicker;
	private readonly Func<CancellationToken, Task> _showLogin;

	[SuppressMessage("Usage", "CA2213", Justification = "由 Disposables 释放")]
	private readonly DanmakuFilterViewModel _liveFilter;

	private int _fileRequest;

	public DanmakuPanelViewModel(IDanmakuAppService danmaku, IRoomAppService roomAppService, ISettingsAppService settingsAppService, IStoragePickerService storagePicker, Func<CancellationToken, Task> showLogin)
	{
		_danmaku = danmaku;
		_roomAppService = roomAppService;
		_settingsAppService = settingsAppService;
		_storagePicker = storagePicker;
		_showLogin = showLogin;
		_liveFilter = new DanmakuFilterViewModel().DisposeWith(Disposables);

		this.WhenChanged(static viewModel => viewModel.IsOpen)
			.Skip(1)
			.Subscribe(_ => Update())
			.DisposeWith(Disposables);

		CloseCommand.DisposeWith(Disposables);
		CloseFileCommand.DisposeWith(Disposables);
		OpenFileCommand.DisposeWith(Disposables);
		LoginCommand.DisposeWith(Disposables);
	}

	internal bool AutoRefresh { get; init; } = true;

	[Reactive]
	public partial bool IsOpen { get; set; }

	[Reactive]
	public partial RoomItemViewModel? Room { get; private set; }

	[Reactive]
	public partial LiveDanmakuViewModel? Live { get; private set; }

	[Reactive]
	public partial DanmakuFileViewModel? File { get; private set; }

	[Reactive]
	public partial bool IsLoadingFile { get; private set; }

	[Reactive]
	public partial string? ErrorMessage { get; private set; }

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

	internal void ShowLive(RoomItemViewModel room)
	{
		DiscardFile();
		Room = room;
		IsOpen = true;
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
		int request = ++_fileRequest;
		ErrorMessage = null;
		IsLoadingFile = true;
		IsOpen = true;
		Update();

		try
		{
			DanmakuFileDto file = await _danmaku.OpenFileAsync(path, cancellationToken);

			if (request != _fileRequest)
			{
				return;
			}

			File?.Dispose();
			File = new DanmakuFileViewModel(file, _danmaku);
		}
		catch (Exception exception) when (DanmakuFileViewModel.IsExpectedFailure(exception))
		{
			if (request == _fileRequest)
			{
				ErrorMessage = DanmakuFileViewModel.DescribeFailure(exception, path);
			}
		}
		finally
		{
			if (request == _fileRequest)
			{
				IsLoadingFile = false;
				Update();
			}
		}
	}

	internal async Task PickFileAsync(RoomItemViewModel? room, CancellationToken cancellationToken)
	{
		string directory;

		if (room is null && File is { } file)
		{
			directory = Path.GetDirectoryName(file.Path) ?? string.Empty;
		}
		else if ((room ?? Room) is { } target)
		{
			directory = await _roomAppService.GetDirectoryAsync(target.RoomId, cancellationToken);
		}
		else
		{
			directory = (await _settingsAppService.GetAsync(cancellationToken)).RecordDirectory;
		}

		if (await _storagePicker.PickDanmakuFileAsync(directory) is { } path)
		{
			await LoadFileAsync(path, cancellationToken);
		}
	}

	[ReactiveCommand]
	private Task OpenFileAsync(CancellationToken cancellationToken)
	{
		return PickFileAsync(null, cancellationToken);
	}

	[ReactiveCommand]
	private void CloseFile()
	{
		DiscardFile();
		Update();
	}

	[ReactiveCommand]
	private void Close()
	{
		IsOpen = false;
	}

	// Cookie 换成新的时由房间列表调用，LoginAsync 不要自己调
	internal void OnLoggedIn()
	{
		Live?.OnLoggedIn();
	}

	[ReactiveCommand]
	private Task LoginAsync(CancellationToken cancellationToken)
	{
		return _showLogin(cancellationToken);
	}

	private void DiscardFile()
	{
		++_fileRequest;
		IsLoadingFile = false;
		File?.Dispose();
		File = null;
		ErrorMessage = null;
	}

	private void Update()
	{
		if (!IsOpen)
		{
			DiscardFile();
		}

		long? roomId = IsOpen && File is null && !IsLoadingFile ? Room?.RoomId : null;

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
			Live = new LiveDanmakuViewModel(_danmaku.Subscribe(id), _liveFilter, AutoRefresh);
		}
		catch (KeyNotFoundException)
		{
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
