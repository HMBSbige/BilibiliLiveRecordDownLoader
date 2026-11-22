namespace BilibiliLiveRecordDownLoader.ViewModels.Danmaku;

// 一个房间的实时弹幕；释放时退订，房间不录弹幕时就不再为它解析
public sealed partial class LiveDanmakuViewModel : ViewModelBase
{
	// 每秒约 7 次，新弹幕成批加入列表；再快滚动跟不上，再慢看起来一顿一顿的
	internal static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(150);

	private readonly IDanmakuFeed _feed;
	private readonly List<DanmakuEntryViewModel> _all = [];
	private long _lastSequence;
	private long? _lastVersion;
	private DanmakuFeedStatus? _status;

	// autoRefresh 为 false 时由调用方调用 Refresh，测试用
	public LiveDanmakuViewModel(IDanmakuFeed feed, bool autoRefresh = true)
	{
		_feed = feed;
		Filter = new DanmakuFilterViewModel().DisposeWith(Disposables);
		Filter.CriteriaChanged.Subscribe(_ => Rebuild()).DisposeWith(Disposables);
		ScrollToLatestCommand.DisposeWith(Disposables);

		this.WhenChanged(static viewModel => viewModel.IsFollowing)
			.Subscribe
			(isFollowing =>
				{
					if (isFollowing)
					{
						NewEntryCount = 0;
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

	// 按过滤条件显示的条目，从旧到新
	public DanmakuEntryCollection Entries { get; } = [];

	[Reactive]
	public partial DanmakuConnectionState State { get; private set; }

	[Reactive]
	public partial string StateText { get; private set; } = string.Empty;

	// 还没连上过时为 null
	[Reactive]
	public partial string? IdentityText { get; private set; }

	// 匿名连接看不到昵称，登录后重连就能看到
	[Reactive]
	public partial bool CanLogin { get; private set; }

	[Reactive]
	public partial string RateText { get; private set; } = string.Empty;

	// 连接断开等待重连时显示；连上后为 null
	[Reactive]
	public partial string? ReconnectingText { get; private set; }

	[Reactive]
	public partial bool HasEntries { get; private set; }

	[Reactive]
	public partial bool HasVisibleEntries { get; private set; }

	// 停在底部时跟随新消息；视图在用户往上翻时设为 false，回到底部时设为 true
	[Reactive]
	public partial bool IsFollowing { get; set; } = true;

	// 不跟随期间新加入列表的条数
	[Reactive]
	public partial int NewEntryCount { get; private set; }

	[Reactive]
	public partial string? NewEntriesText { get; private set; }

	// 版本号没变时只更新状态，接收速率随时间变化但不计入版本号
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

	[ReactiveCommand]
	private void ScrollToLatest()
	{
		IsFollowing = true;
	}

	private void Append(IReadOnlyList<LiveDanmakuEntryDto> entries)
	{
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

			if (Filter.Matches(row))
			{
				visible.Add(row);
			}
		}

		Entries.AddRange(visible);
		Trim();

		if (!IsFollowing)
		{
			NewEntryCount += visible.Count;
		}

		UpdateState();
	}

	// 只保留最近的条目，和应用服务保留的条数一致
	private void Trim()
	{
		int excess = _all.Count - DanmakuConsts.MaxFeedEntries;

		if (excess <= 0)
		{
			return;
		}

		long lastRemoved = _all[excess - 1].Sequence;
		_all.RemoveRange(0, excess);

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
		Entries.Reset(_all.Where(Filter.Matches));
		IsFollowing = true;
		NewEntryCount = 0;
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
		IdentityText = DanmakuEntryViewModel.FormatIdentity(status.UserId, status.IsMasked);
		CanLogin = status is { UserId: 0, IsMasked: true };
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
