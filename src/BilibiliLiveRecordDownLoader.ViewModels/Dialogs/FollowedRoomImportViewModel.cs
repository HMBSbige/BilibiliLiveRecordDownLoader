using System.Diagnostics;

namespace BilibiliLiveRecordDownLoader.ViewModels.Dialogs;

public sealed partial class FollowedRoomImportViewModel : DialogViewModel<bool>
{
	private readonly IRoomAppService _roomAppService;
	private readonly INotificationService _notifications;
	private readonly Action _showLogin;
	private readonly RoomSettingsDto _defaults = new() { RoomId = 0 };
	private readonly List<FollowedRoomItemViewModel> _rooms = [];
	private bool? _areAllVisibleSelected = false;
	private bool _isSelectingVisible;

	public FollowedRoomImportViewModel(IRoomAppService roomAppService, INotificationService notifications, Action showLogin)
	{
		_roomAppService = roomAppService;
		_notifications = notifications;
		_showLogin = showLogin;
		IsRecordingEnabled = _defaults.IsRecordingEnabled;
		IsNotificationEnabled = _defaults.IsNotificationEnabled;

		CanImport = this.WhenChanged(static viewModel => viewModel.State, static viewModel => viewModel.SelectedCount, static (state, count) => state is FollowedRoomLoadState.Loaded && count > 0);

		this.WhenChanged(static viewModel => viewModel.SearchText)
			.Skip(1)
			.Subscribe(_ => ApplyFilter())
			.DisposeWith(Disposables);

		OnLanguageChanged(UpdateTexts);
		UpdateTexts();

		ImportCommand.DisposeWith(Disposables);
		ReloadCommand.DisposeWith(Disposables);
		LoginCommand.DisposeWith(Disposables);
	}

	public override string Title => AppLanguage.Current.ImportFollowedRooms;

	// 按搜索条件显示的房间
	public ObservableCollection<FollowedRoomItemViewModel> VisibleRooms { get; } = [];

	public IObservable<bool> CanImport { get; }

	[Reactive]
	public partial FollowedRoomLoadState State { get; private set; }

	[Reactive]
	public partial string? SearchText { get; set; }

	// 统一作用于本次导入的房间；开启录制时，导入那一刻正在直播的房间都会立即开录
	[Reactive]
	public partial bool IsRecordingEnabled { get; set; }

	[Reactive]
	public partial bool IsNotificationEnabled { get; set; }

	[Reactive]
	public partial bool HasRooms { get; private set; }

	[Reactive]
	public partial bool IsLoading { get; private set; }

	[Reactive]
	public partial bool IsEmpty { get; private set; }

	[Reactive]
	public partial bool HasVisibleRooms { get; private set; }

	[Reactive]
	public partial bool HasSelectableVisibleRooms { get; private set; }

	[Reactive]
	public partial int SelectedCount { get; private set; }

	[Reactive]
	public partial string ImportText { get; private set; } = string.Empty;

	// 加载中显示进度，加载完成后显示总数
	[Reactive]
	public partial string? SummaryText { get; private set; }

	[Reactive]
	public partial string? LoadError { get; private set; }

	[Reactive]
	public partial string? ErrorMessage { get; private set; }

	// 部分选中时为 null；设置时选中或取消当前可见的全部房间
	public bool? AreAllVisibleSelected
	{
		get => _areAllVisibleSelected;
		set
		{
			_isSelectingVisible = true;

			try
			{
				foreach (FollowedRoomItemViewModel room in VisibleRooms)
				{
					room.IsSelected = value is true;
				}
			}
			finally
			{
				_isSelectingVisible = false;
			}

			UpdateSelection();
			// 复选框自己切换后的值可能与实际选中状态不同，比如可见的房间都已添加
			this.RaisePropertyChanged();
		}
	}

	public override void OnOpened()
	{
		Forget(LoadAsync);
	}

	[ReactiveCommand]
	private void Reload()
	{
		Forget(LoadAsync);
	}

	[ReactiveCommand]
	private void Login()
	{
		Dismiss();
		_showLogin();
	}

	// 全部成功才关闭；有失败时保留对话框，已导入的房间标记为已添加，失败的仍保持选中以便重试
	[ReactiveCommand(CanExecute = nameof(CanImport))]
	private async Task ImportAsync(CancellationToken cancellationToken)
	{
		FollowedRoomItemViewModel[] selected = [.. _rooms.Where(static room => room.IsSelected)];

		if (selected.Length is 0)
		{
			return;
		}

		ErrorMessage = null;
		IsBusy = true;

		try
		{
			// 除这两个开关外与手动添加时的默认设置相同，之后可以在编辑界面里修改
			RoomSettingsDto settings = _defaults with { IsRecordingEnabled = IsRecordingEnabled, IsNotificationEnabled = IsNotificationEnabled };
			IReadOnlyList<RoomAddResult> results = await _roomAppService.AddRangeAsync([.. selected.Select(room => settings with { RoomId = room.RoomId })], cancellationToken);
			List<string> failures = [];
			int added = 0;

			for (int index = 0; index < selected.Length; ++index)
			{
				switch (results[index])
				{
					case RoomAddResult.Added:
					{
						++added;
						selected[index].MarkAdded();
						break;
					}
					case RoomAddResult.Skipped:
					{
						selected[index].MarkAdded();
						break;
					}
					case RoomAddResult.Failed failed:
					{
						failures.Add($"{selected[index].DisplayName}: {failed.Error}");
						break;
					}
					default:
					{
						throw new UnreachableException();
					}
				}
			}

			if (added > 0)
			{
				_notifications.Show(AppLanguage.Current.ImportFollowedRooms, AppLanguage.Current.RoomsImported.ToString(added.ToString(CultureInfo.CurrentCulture)), AppNotificationType.Success);
			}

			if (failures.Count is 0)
			{
				Close(true);
				return;
			}

			ErrorMessage = string.Join(Environment.NewLine, [AppLanguage.Current.RoomsImportFailed.ToString(failures.Count.ToString(CultureInfo.CurrentCulture)), .. failures]);
			UpdateSelection();
		}
		catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
		{
			ErrorMessage = exception.Message;
		}
		finally
		{
			IsBusy = false;
		}
	}

	private async Task LoadAsync(CancellationToken cancellationToken)
	{
		ClearRooms();
		LoadError = null;
		ErrorMessage = null;
		State = FollowedRoomLoadState.Loading;
		UpdateTexts();

		try
		{
			// 关注较多时要翻很多页，边加载边显示
			await foreach (FollowedRoomDto dto in _roomAppService.GetFollowedRoomsAsync(cancellationToken))
			{
				FollowedRoomItemViewModel room = new(dto, OnSelectionChanged);
				_rooms.Add(room);

				if (Matches(room))
				{
					VisibleRooms.Add(room);
				}

				UpdateTexts();
			}

			State = FollowedRoomLoadState.Loaded;
		}
		catch (LoginRequiredException)
		{
			ClearRooms();
			State = FollowedRoomLoadState.LoginRequired;
		}
		catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
		{
			// 不保留加载了一半的列表，重试时从头加载
			ClearRooms();
			LoadError = exception.Message;
			State = FollowedRoomLoadState.Failed;
		}

		UpdateTexts();
	}

	private void ClearRooms()
	{
		_rooms.Clear();
		VisibleRooms.Clear();
	}

	private void ApplyFilter()
	{
		VisibleRooms.Clear();

		foreach (FollowedRoomItemViewModel room in _rooms.Where(Matches))
		{
			VisibleRooms.Add(room);
		}

		UpdateTexts();
	}

	private bool Matches(FollowedRoomItemViewModel room)
	{
		return SearchText?.Trim() is not { Length: > 0 } keyword || room.Matches(keyword);
	}

	private void OnSelectionChanged()
	{
		if (!_isSelectingVisible)
		{
			UpdateSelection();
		}
	}

	private void UpdateTexts()
	{
		HasRooms = _rooms.Count > 0;
		HasVisibleRooms = VisibleRooms.Count > 0;
		IsLoading = State is FollowedRoomLoadState.Loading;
		IsEmpty = State is FollowedRoomLoadState.Loaded && !HasRooms;

		string total = _rooms.Count.ToString(CultureInfo.CurrentCulture);
		SummaryText = State switch
		{
			FollowedRoomLoadState.Loading => AppLanguage.Current.LoadingFollowedRooms.ToString(total),
			FollowedRoomLoadState.Loaded when HasRooms => AppLanguage.Current.FollowedRoomsSummary.ToString(total, _rooms.Count(static room => room.LiveStatus is LiveStatus.Live).ToString(CultureInfo.CurrentCulture)),
			_ => null
		};

		UpdateSelection();
	}

	private void UpdateSelection()
	{
		SelectedCount = _rooms.Count(static room => room.IsSelected);
		ImportText = SelectedCount is 0 ? AppLanguage.Current.Import : AppLanguage.Current.ImportCount.ToString(SelectedCount.ToString(CultureInfo.CurrentCulture));

		FollowedRoomItemViewModel[] selectable = [.. VisibleRooms.Where(static room => !room.IsAdded)];
		HasSelectableVisibleRooms = selectable.Length > 0;

		bool? areAllVisibleSelected = selectable.Count(static room => room.IsSelected) switch
		{
			0 => false,
			var count when count == selectable.Length => true,
			_ => null
		};

		if (_areAllVisibleSelected != areAllVisibleSelected)
		{
			_areAllVisibleSelected = areAllVisibleSelected;
			this.RaisePropertyChanged(nameof(AreAllVisibleSelected));
		}
	}
}
