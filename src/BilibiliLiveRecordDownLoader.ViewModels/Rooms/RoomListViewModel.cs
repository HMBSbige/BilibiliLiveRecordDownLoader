using System.Collections.Specialized;

namespace BilibiliLiveRecordDownLoader.ViewModels.Rooms;

public sealed partial class RoomListViewModel : ViewModelBase
{
	private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(500);

	private readonly IRoomAppService _roomAppService;
	private readonly IAppConfigManager _configManager;
	private readonly Dictionary<long, RoomItemViewModel> _rooms = [];
	private AppConfig? _config;
	private string? _cookie;
	private bool _isArranging;

	public RoomListViewModel(IRoomAppService roomAppService, IAppConfigManager configManager, IDanmakuAppService danmakuAppService)
	{
		_roomAppService = roomAppService;
		_configManager = configManager;
		Danmaku = new DanmakuPanelViewModel(danmakuAppService, configManager, LoginAndSyncAsync).DisposeWith(Disposables);
		HasSelection = this.WhenChanged(static viewModel => viewModel.SelectedCount).Map(static count => count > 0);
		HasSingleSelection = this.WhenChanged(static viewModel => viewModel.SelectedCount).Map(static count => count is 1);
		CanMoveSelection = this.WhenChanged(static viewModel => viewModel.CanReorder, static viewModel => viewModel.SelectedCount, static (canReorder, count) => canReorder && count > 0);
		SelectedRooms.CollectionChanged += OnSelectionChanged;
		OnLanguageChanged(UpdateSelection);

		this.WhenChanged(static viewModel => viewModel.SortOrder, static viewModel => viewModel.Filter, static (sortOrder, filter) => (sortOrder, filter))
			.Skip(1)
			.Subscribe(_ => Arrange())
			.DisposeWith(Disposables);

		this.WhenChanged(static viewModel => viewModel.SortOrder, static viewModel => viewModel.Filter, static (sortOrder, filter) => sortOrder is RoomSortOrder.Custom && filter is RoomFilter.All)
			.Subscribe(canReorder => CanReorder = canReorder)
			.DisposeWith(Disposables);

		AddRoomCommand.DisposeWith(Disposables);
		ImportFollowedRoomsCommand.DisposeWith(Disposables);
		EditRoomCommand.DisposeWith(Disposables);
		RemoveRoomsCommand.DisposeWith(Disposables);
		RefreshRoomsCommand.DisposeWith(Disposables);
		StartRecordingCommand.DisposeWith(Disposables);
		StopRecordingCommand.DisposeWith(Disposables);
		OpenRoomFolderCommand.DisposeWith(Disposables);
		OpenLiveRoomCommand.DisposeWith(Disposables);
		ShowDanmakuCommand.DisposeWith(Disposables);
		OpenDanmakuFileCommand.DisposeWith(Disposables);
		SortByCommand.DisposeWith(Disposables);
		FilterByCommand.DisposeWith(Disposables);
		MoveToTopCommand.DisposeWith(Disposables);
		MoveToBottomCommand.DisposeWith(Disposables);
		MoveUpCommand.DisposeWith(Disposables);
		MoveDownCommand.DisposeWith(Disposables);
		MoveSelectionToCommand.DisposeWith(Disposables);
		LoginCommand.DisposeWith(Disposables);

		RoomsMoved = Signal.Merge<IReadOnlyList<RoomItemViewModel>>(MoveToTopCommand, MoveToBottomCommand, MoveUpCommand, MoveDownCommand, MoveSelectionToCommand);
	}

	// 列表刷新之后发出，按显示顺序
	public IObservable<IReadOnlyList<RoomItemViewModel>> RoomsMoved { get; }

	public ObservableCollection<RoomItemViewModel> Rooms { get; } = [];

	public ObservableCollection<RoomItemViewModel> SelectedRooms { get; } = [];

	public IObservable<bool> HasSelection { get; }

	public IObservable<bool> HasSingleSelection { get; }

	public IObservable<bool> CanMoveSelection { get; }

	public DanmakuPanelViewModel Danmaku { get; }

	[Reactive]
	public partial bool HasRooms { get; private set; }

	[Reactive]
	public partial bool HasVisibleRooms { get; private set; }

	[Reactive]
	public partial int SelectedCount { get; private set; }

	[Reactive]
	public partial string? SelectionText { get; private set; }

	[Reactive]
	public partial RoomSortOrder SortOrder { get; private set; }

	[Reactive]
	public partial RoomFilter Filter { get; private set; }

	// 只有按自定义顺序显示全部直播间时，列表中的位置才对应自定义顺序
	[Reactive]
	public partial bool CanReorder { get; private set; }

	[Reactive]
	public partial string? RecordingSummary { get; private set; }

	[Reactive]
	public partial string? RecordingSpeed { get; private set; }

	[Reactive]
	public partial string? LoginBannerText { get; private set; }

	// 为 null 时交给全局处理
	internal Action<Exception>? ReportSaveFailure { get; init; }

	// 返回是否登录成功
	internal Func<CancellationToken, Task<bool>> ShowLogin { get; init; } = static cancellationToken => AppLocator.Current.GetRequiredService<AccountViewModel>().LoginWithQrCodeAsync(cancellationToken);

	internal void ApplyConfig(AppConfig config)
	{
		// 配置管理器一直返回同一个实例，Cookie 的改动也在它上面
		_config = config;
		SortOrder = config.RoomSortOrder;
		PersistToConfig(_configManager, this.WhenChanged(static viewModel => viewModel.SortOrder), static (appConfig, value) => appConfig.RoomSortOrder = value);
	}

	internal void StartMonitoring()
	{
		PollChanges(() => _roomAppService.Version, Sync, RefreshInterval);
		OnLanguageChanged(Sync);

		// 在账号页登录、粘贴或清除 Cookie 都不经过这里，房间的版本也不变
		_cookie = _config?.Cookie;
		Poll(() => _config?.Cookie, OnCookieChanged, RefreshInterval);
	}

	[ReactiveCommand]
	private async Task AddRoomAsync(CancellationToken cancellationToken)
	{
		AppConfig config = await _configManager.GetAsync(cancellationToken);

		if (await AppLocator.Current.GetRequiredService<DialogHostViewModel>().ShowAsync(new RoomEditorViewModel(null, config, _roomAppService.AddAsync)) is not null)
		{
			Sync();
		}
	}

	// 导入可能部分成功
	[ReactiveCommand]
	private async Task ImportFollowedRoomsAsync()
	{
		await AppLocator.Current.GetRequiredService<DialogHostViewModel>().ShowAsync(new FollowedRoomImportViewModel(_roomAppService, AppLocator.Current.GetRequiredService<INotificationService>(), static () => AppLocator.Current.GetRequiredService<MainWindowViewModel>().Navigate(MainPage.Account)));
		Sync();
	}

	[ReactiveCommand(CanExecute = nameof(HasSingleSelection))]
	private async Task EditRoomAsync(CancellationToken cancellationToken)
	{
		if (SelectedRooms is not [{ } room])
		{
			return;
		}

		AppConfig config = await _configManager.GetAsync(cancellationToken);

		// 行内开关要基于最新设置修改
		if (await AppLocator.Current.GetRequiredService<DialogHostViewModel>().ShowAsync(new RoomEditorViewModel(room.Settings, config, _roomAppService.UpdateAsync)) is not null)
		{
			Sync();
		}
	}

	[ReactiveCommand(CanExecute = nameof(HasSelection))]
	private async Task RemoveRoomsAsync(CancellationToken cancellationToken)
	{
		RoomItemViewModel[] rooms = [.. SelectedRooms];
		string names = string.Join(Environment.NewLine, rooms.Select(static room => room.DisplayName));

		if (rooms.Length > 0 && await AppLocator.Current.GetRequiredService<DialogHostViewModel>().ConfirmAsync(AppLanguage.Current.RemoveRoomsConfirm, names, AppLanguage.Current.RemoveRooms, true))
		{
			await _roomAppService.RemoveAsync([.. rooms.Select(static room => room.RoomId)], cancellationToken);
		}
	}

	[ReactiveCommand]
	private async Task RefreshRoomsAsync(CancellationToken cancellationToken)
	{
		IEnumerable<RoomItemViewModel> targets = SelectedRooms.Count is 0 ? _rooms.Values : SelectedRooms;

		await _roomAppService.RefreshAsync([.. targets.Select(static room => room.RoomId)], cancellationToken);
	}

	[ReactiveCommand(CanExecute = nameof(HasSelection))]
	private void StartRecording()
	{
		SetRecordingEnabled(true);
	}

	[ReactiveCommand(CanExecute = nameof(HasSelection))]
	private void StopRecording()
	{
		SetRecordingEnabled(false);
	}

	[ReactiveCommand(CanExecute = nameof(HasSingleSelection))]
	private async Task OpenRoomFolderAsync(CancellationToken cancellationToken)
	{
		if (SelectedRooms is not [{ } room])
		{
			return;
		}

		AppConfig config = await _configManager.GetAsync(cancellationToken);
		string directory = RecordingFileLayout.GetRoomDirectory(config.RecordDirectory, room.RoomId);

		await AppLocator.Current.GetRequiredService<ILauncherService>().OpenFolderAsync(directory);
	}

	[ReactiveCommand(CanExecute = nameof(HasSingleSelection))]
	private async Task OpenLiveRoomAsync()
	{
		if (SelectedRooms is [{ } room])
		{
			await AppLocator.Current.GetRequiredService<ILauncherService>().LaunchUriAsync(new Uri(AppConsts.LiveRoomUrlPrefix + room.RoomId.ToString(CultureInfo.InvariantCulture)));
		}
	}

	[ReactiveCommand(CanExecute = nameof(HasSingleSelection))]
	private void ShowDanmaku()
	{
		if (SelectedRooms is [{ } room])
		{
			Danmaku.ShowLive(room);
		}
	}

	[ReactiveCommand(CanExecute = nameof(HasSingleSelection))]
	private async Task OpenDanmakuFileAsync(CancellationToken cancellationToken)
	{
		if (SelectedRooms is not [{ } room])
		{
			return;
		}

		Danmaku.Follow(room);
		await Danmaku.PickFileAsync(room, cancellationToken);
	}

	[ReactiveCommand]
	private Task LoginAsync(CancellationToken cancellationToken)
	{
		return LoginAndSyncAsync(cancellationToken);
	}

	// 在录的直播间和弹幕连接都要等重连才用上登录身份，横幅按 Cookie 立即收起，面板先显示等重连
	private async Task<bool> LoginAndSyncAsync(CancellationToken cancellationToken)
	{
		if (!await ShowLogin(cancellationToken))
		{
			return false;
		}

		Sync();
		Danmaku.OnLoggedIn();
		return true;
	}

	// 从这里登录时 LoginAndSyncAsync 已经处理过，再处理一次结果相同
	private void OnCookieChanged(string? cookie)
	{
		if (cookie == _cookie)
		{
			return;
		}

		_cookie = cookie;
		Sync();

		if (!string.IsNullOrWhiteSpace(cookie))
		{
			Danmaku.OnLoggedIn();
		}
	}

	[ReactiveCommand]
	private void SortBy(RoomSortOrder sortOrder)
	{
		SortOrder = sortOrder;
	}

	[ReactiveCommand]
	private void FilterBy(RoomFilter filter)
	{
		Filter = filter;
	}

	[ReactiveCommand(CanExecute = nameof(CanMoveSelection))]
	private Task<IReadOnlyList<RoomItemViewModel>> MoveToTopAsync(CancellationToken cancellationToken)
	{
		return MoveSelectionAsync(RoomReorder.Top, cancellationToken);
	}

	[ReactiveCommand(CanExecute = nameof(CanMoveSelection))]
	private Task<IReadOnlyList<RoomItemViewModel>> MoveToBottomAsync(CancellationToken cancellationToken)
	{
		return MoveSelectionAsync(static (_, _) => null, cancellationToken);
	}

	[ReactiveCommand(CanExecute = nameof(CanMoveSelection))]
	private Task<IReadOnlyList<RoomItemViewModel>> MoveUpAsync(CancellationToken cancellationToken)
	{
		return MoveSelectionAsync(RoomReorder.Up, cancellationToken);
	}

	[ReactiveCommand(CanExecute = nameof(CanMoveSelection))]
	private Task<IReadOnlyList<RoomItemViewModel>> MoveDownAsync(CancellationToken cancellationToken)
	{
		return MoveSelectionAsync(RoomReorder.Down, cancellationToken);
	}

	// 参数是在 Rooms 中的插入位置，取值 0 到 Rooms.Count
	[ReactiveCommand(CanExecute = nameof(CanMoveSelection))]
	private Task<IReadOnlyList<RoomItemViewModel>> MoveSelectionToAsync(int index, CancellationToken cancellationToken)
	{
		return MoveSelectionAsync((order, moving) => RoomReorder.At(order, moving, index), cancellationToken);
	}

	// 每行自己保存开关，失败时各自回滚
	private void SetRecordingEnabled(bool enabled)
	{
		foreach (RoomItemViewModel room in SelectedRooms.ToArray())
		{
			room.IsRecordingEnabled = enabled;
		}
	}

	private void OnSelectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (!_isArranging)
		{
			UpdateSelection();
		}
	}

	private void UpdateSelection()
	{
		SelectedCount = SelectedRooms.Count;

		if (SelectedRooms is [var room])
		{
			Danmaku.Follow(room);
		}

		SelectionText = SelectedCount is 0 ? null : AppLanguage.Current.SelectedCount.ToString(SelectedCount.ToString(CultureInfo.CurrentCulture));
	}

	private async Task<IReadOnlyList<RoomItemViewModel>> MoveSelectionAsync(Func<IReadOnlyList<long>, IReadOnlySet<long>, long?> getBefore, CancellationToken cancellationToken)
	{
		HashSet<RoomItemViewModel> selected = [.. SelectedRooms];
		RoomItemViewModel[] moving = [.. Rooms.Where(selected.Contains)];

		if (!CanReorder || moving.Length is 0)
		{
			return [];
		}

		long[] order = [.. Rooms.Select(static room => room.RoomId)];
		HashSet<long> roomIds = [.. moving.Select(static room => room.RoomId)];

		await _roomAppService.MoveAsync(roomIds, getBefore(order, roomIds), cancellationToken);
		Sync();
		return moving;
	}

	internal void Sync()
	{
		Dictionary<long, RoomSnapshot> latest = [];

		foreach (RoomSnapshot snapshot in _roomAppService.GetRooms())
		{
			latest.TryAdd(snapshot.RoomId, snapshot);
		}

		foreach (RoomItemViewModel removed in _rooms.Values.Where(room => !latest.ContainsKey(room.RoomId)).ToArray())
		{
			_rooms.Remove(removed.RoomId);
			Rooms.Remove(removed);
			// 不依赖列表控件移除条目时顺带取消选中，免得面板又跟随已移除的房间
			SelectedRooms.Remove(removed);
			Danmaku.OnRoomRemoved(removed);
			removed.Dispose();
		}

		int order = 0;
		bool hasCookie = !string.IsNullOrWhiteSpace(_config?.Cookie);

		foreach (RoomSnapshot snapshot in latest.Values)
		{
			if (_rooms.TryGetValue(snapshot.RoomId, out RoomItemViewModel? room))
			{
				room.Apply(snapshot, hasCookie);
			}
			else
			{
				room = new RoomItemViewModel(snapshot, hasCookie) { RoomAppService = _roomAppService, ReportSaveFailure = ReportSaveFailure };
				_rooms.Add(snapshot.RoomId, room);
			}

			room.Order = order++;
		}

		HasRooms = _rooms.Count > 0;
		RoomSnapshot[] recording = [.. latest.Values.Where(static snapshot => snapshot.RecordStatus is RecordStatus.Recording)];
		Summarize(recording);
		UpdateLoginBanner(latest.Values, recording, hasCookie);
		Arrange();
	}

	// 只移动位置变化的项以保留滚动位置；ListBox 会取消选中被移动的项，重排后恢复
	private void Arrange()
	{
		RoomItemViewModel[] visible = [.. _rooms.Values.Where(Matches).Order(Comparer<RoomItemViewModel>.Create(Compare))];
		HashSet<RoomItemViewModel> visibleSet = [.. visible];
		RoomItemViewModel[] selected = [.. SelectedRooms.Where(visibleSet.Contains)];

		_isArranging = true;

		try
		{
			for (int index = Rooms.Count - 1; index >= 0; --index)
			{
				if (!visibleSet.Contains(Rooms[index]))
				{
					Rooms.RemoveAt(index);
				}
			}

			for (int index = 0; index < visible.Length; ++index)
			{
				int current = Rooms.IndexOf(visible[index]);

				if (current < 0)
				{
					Rooms.Insert(index, visible[index]);
				}
				else if (current != index)
				{
					Rooms.Move(current, index);
				}
			}

			foreach (RoomItemViewModel room in selected.Where(room => !SelectedRooms.Contains(room)))
			{
				SelectedRooms.Add(room);
			}
		}
		finally
		{
			_isArranging = false;
		}

		UpdateSelection();
		HasVisibleRooms = Rooms.Count > 0;
	}

	private bool Matches(RoomItemViewModel room)
	{
		return Filter switch
		{
			RoomFilter.Live => room.LiveStatus is LiveStatus.Live,
			RoomFilter.Recording => room.RecordStatus is not RecordStatus.Idle,
			_ => true
		};
	}

	private int Compare(RoomItemViewModel x, RoomItemViewModel y)
	{
		int result = SortOrder switch
		{
			RoomSortOrder.Status => Rank(x).CompareTo(Rank(y)) is var rank and not 0 ? rank : CompareNames(x, y),
			RoomSortOrder.Name => CompareNames(x, y),
			RoomSortOrder.RoomId => x.RoomId.CompareTo(y.RoomId),
			_ => x.Order.CompareTo(y.Order)
		};

		return result is 0 ? x.RoomId.CompareTo(y.RoomId) : result;

		static int CompareNames(RoomItemViewModel x, RoomItemViewModel y)
		{
			return string.Compare(x.DisplayName, y.DisplayName, StringComparison.CurrentCultureIgnoreCase);
		}

		static int Rank(RoomItemViewModel room)
		{
			return (room.RecordStatus, room.LiveStatus) switch
			{
				(RecordStatus.Recording, _) => 0,
				(RecordStatus.Connecting, _) => 1,
				(_, LiveStatus.Live) => 2,
				(_, LiveStatus.Round) => 3,
				_ => 4
			};
		}
	}

	// 编码原因和登录可能失效都不是登录能解决的；仅录弹幕时视频不在录，所以弹幕看全部直播间
	private void UpdateLoginBanner(IEnumerable<RoomSnapshot> rooms, RoomSnapshot[] recording, bool hasCookie)
	{
		int restricted = hasCookie ? 0 : recording.Count(static snapshot => snapshot.Recording?.Restriction is QualityRestriction.NotLoggedIn);
		bool isDanmakuMasked = !hasCookie && rooms.Any(static snapshot => snapshot.Danmaku.IsRecording);
		string count = restricted.ToString(CultureInfo.CurrentCulture);

		LoginBannerText = (restricted, isDanmakuMasked) switch
		{
			(0, false) => null,
			(0, true) => AppLanguage.Current.DanmakuMaskedBanner,
			(_, false) => AppLanguage.Current.QualityRestrictedBanner.ToString(count),
			_ => AppLanguage.Current.QualityRestrictedAndDanmakuMaskedBanner.ToString(count)
		};
	}

	private void Summarize(RoomSnapshot[] recording)
	{
		RecordingSummary = recording.Length is 0 ? null : AppLanguage.Current.RecordingSummary.ToString(recording.Length.ToString(CultureInfo.CurrentCulture));
		RecordingSpeed = recording.Length is 0 ? null : ByteSize.Format(recording.Sum(static snapshot => snapshot.BytesPerSecond)) + "/s";
	}

	public override void Dispose()
	{
		SelectedRooms.CollectionChanged -= OnSelectionChanged;

		foreach (RoomItemViewModel room in _rooms.Values)
		{
			room.Dispose();
		}

		base.Dispose();
	}
}
