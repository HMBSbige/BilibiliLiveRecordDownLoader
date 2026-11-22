namespace BilibiliLiveRecordDownLoader.ViewModels.Danmaku;

// 释放时退订，房间不录弹幕时就不再为它解析
public sealed partial class LiveDanmakuViewModel : ViewModelBase
{
	// 再快滚动跟不上，再慢看起来一顿一顿的
	internal static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(150);

	private readonly IDanmakuFeed _feed;
	private readonly List<DanmakuEntryViewModel> _all = [];
	private long _lastSequence;
	private long? _lastVersion;
	private DanmakuFeedStatus? _status;

	// 不跟随时 Entries 不动：虚拟化列表不做滚动锚定，从头部删条目会让正在读的行滑走
	private bool _isStale;

	// 登录时的连接，要等重连才换成登录身份
	private long? _loginConnection;

	// autoRefresh 为 false 时由测试调用 Refresh
	public LiveDanmakuViewModel(IDanmakuFeed feed, DanmakuFilterViewModel filter, bool autoRefresh = true)
	{
		_feed = feed;
		Filter = filter;
		Filter.CriteriaChanged.Subscribe(_ => Rebuild()).DisposeWith(Disposables);
		ScrollToLatestCommand.DisposeWith(Disposables);

		this.WhenChanged(static viewModel => viewModel.IsFollowing)
			.Subscribe
			(isFollowing =>
				{
					if (!isFollowing)
					{
						return;
					}

					NewEntryCount = 0;

					if (_isStale)
					{
						Reset();
					}
				}
			)
			.DisposeWith(Disposables);

		this.WhenChanged(static viewModel => viewModel.NewEntryCount)
			.Subscribe(_ => UpdateNewEntriesText())
			.DisposeWith(Disposables);

		OnLanguageChanged(UpdateTexts);
		Refresh();

		if (autoRefresh)
		{
			Signal.Every(RefreshInterval, RxSchedulers.TaskpoolScheduler)
				.ObserveOn(RxSchedulers.MainThreadScheduler)
				.Subscribe(_ => Refresh())
				.DisposeWith(Disposables);
		}
	}

	public long RoomId => _feed.RoomId;

	public DanmakuFilterViewModel Filter { get; }

	public DanmakuEntryCollection Entries { get; } = [];

	[Reactive]
	public partial DanmakuConnectionState State { get; private set; }

	[Reactive]
	public partial string StateText { get; private set; } = string.Empty;

	// 还没连上过时为 null
	[Reactive]
	public partial string? IdentityText { get; private set; }

	// 匿名连接看不到昵称，登录后重连才能看到
	[Reactive]
	public partial bool CanLogin { get; private set; }

	[Reactive]
	public partial string RateText { get; private set; } = string.Empty;

	[Reactive]
	public partial string? ReconnectingText { get; private set; }

	[Reactive]
	public partial bool HasEntries { get; private set; }

	[Reactive]
	public partial bool HasVisibleEntries { get; private set; }

	// 视图在用户往上翻时设为 false，回到底部时设为 true
	[Reactive]
	public partial bool IsFollowing { get; set; } = true;

	[Reactive]
	public partial int NewEntryCount { get; private set; }

	[Reactive]
	public partial string? NewEntriesText { get; private set; }

	// 接收速率随时间变化但不计入版本号
	internal void Refresh()
	{
		long version = _feed.Version;

		if (version != _lastVersion)
		{
			_lastVersion = version;
			Append(_feed.GetEntries(_lastSequence));
		}

		_status = _feed.Status;
		UpdateStatus(_status);
	}

	// 连接换掉之前状态里还是匿名，先显示正在等重连
	internal void OnLoggedIn()
	{
		if (_status is { } status)
		{
			_loginConnection = status.Connection;
			UpdateStatus(status);
		}
	}

	[ReactiveCommand]
	private void ScrollToLatest()
	{
		IsFollowing = true;
	}

	private void Append(IReadOnlyList<LiveDanmakuEntryDto> entries)
	{
		Func<DanmakuEntryViewModel, bool> matches = Filter.CreatePredicate();
		List<DanmakuEntryViewModel> visible = [];

		foreach (LiveDanmakuEntryDto entry in entries)
		{
			if (entry.Sequence <= _lastSequence)
			{
				continue;
			}

			_lastSequence = entry.Sequence;

			if (entry.Entry is DanmakuEntryDto.SuperChatDeletion deletion)
			{
				DanmakuEntryViewModel.MarkDeleted(_all, deletion.Ids);
				continue;
			}

			DanmakuEntryViewModel row = DanmakuEntryViewModel.FromLive(entry);
			_all.Add(row);

			if (matches(row))
			{
				visible.Add(row);
			}
		}

		if (IsFollowing)
		{
			Entries.AddRange(visible);
		}
		else if (visible.Count > 0)
		{
			_isStale = true;
			NewEntryCount += visible.Count;
		}

		Trim();
		UpdateState();
	}

	private void Trim()
	{
		int excess = _all.Count - DanmakuConsts.MaxFeedEntries;

		if (excess <= 0)
		{
			return;
		}

		long lastRemoved = _all[excess - 1].Sequence;
		_all.RemoveRange(0, excess);

		if (!IsFollowing)
		{
			_isStale = true;
			return;
		}

		int removed = 0;

		while (removed < Entries.Count && Entries[removed].Sequence <= lastRemoved)
		{
			++removed;
		}

		Entries.RemoveFirst(removed);
	}

	// 重建后原来的滚动位置没有意义
	private void Rebuild()
	{
		Reset();
		IsFollowing = true;
		NewEntryCount = 0;
	}

	private void Reset()
	{
		_isStale = false;
		Entries.Reset(_all.Where(Filter.CreatePredicate()));
		UpdateState();
	}

	private void UpdateState()
	{
		HasEntries = _all.Count > 0;
		HasVisibleEntries = Entries.Count > 0;
	}

	private void UpdateStatus(DanmakuFeedStatus status)
	{
		State = status.State;
		StateText = status.State switch
		{
			DanmakuConnectionState.Connected => AppLanguage.Current.DanmakuConnected,
			DanmakuConnectionState.Disconnected => AppLanguage.Current.DanmakuDisconnected,
			_ => AppLanguage.Current.DanmakuConnecting
		};
		bool isMaskedAnonymous = status is { UserId: 0, IsMasked: true };
		bool isLoginPending = isMaskedAnonymous && status.Connection == _loginConnection;
		IdentityText = isLoginPending ? AppLanguage.Current.DanmakuLoginPending : DanmakuEntryViewModel.FormatIdentity(status.UserId, status.IsMasked);
		CanLogin = isMaskedAnonymous && !isLoginPending;
		RateText = AppLanguage.Current.DanmakuRate.ToString(Math.Round(status.MessagesPerMinute).ToString("0", CultureInfo.CurrentCulture));
		ReconnectingText = status.DisconnectedAt is { } disconnectedAt ? AppLanguage.Current.DanmakuReconnecting.ToString(disconnectedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)) : null;
	}

	private void UpdateNewEntriesText()
	{
		NewEntriesText = NewEntryCount is 0 ? null : AppLanguage.Current.NewDanmakuCount.ToString(NewEntryCount.ToString(CultureInfo.CurrentCulture));
	}

	private void UpdateTexts()
	{
		if (_status is { } status)
		{
			UpdateStatus(status);
		}

		UpdateNewEntriesText();
	}

	public override void Dispose()
	{
		base.Dispose();
		_feed.Dispose();
	}
}
