using System.Diagnostics.CodeAnalysis;

namespace BilibiliLiveRecordDownLoader.ViewModels.Danmaku;

// 释放时退订，房间不录弹幕时就不再为它解析
public sealed partial class LiveDanmakuViewModel : ViewModelBase
{
	// 再快滚动跟不上，再慢看起来一顿一顿的
	private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(150);

	[SuppressMessage("Usage", "CA2213", Justification = "由 Disposables 释放")]
	private readonly IDanmakuFeed _feed;
	private readonly List<DanmakuEntryViewModel> _all = [];
	private long _lastSequence;
	private long? _lastVersion;
	private DanmakuFeedStatus _status;

	// 不跟随时 Entries 不动：虚拟化列表不做滚动锚定，从头部删条目会让正在读的行滑走
	private bool _isStale;

	// 登录时的连接，要等重连才换成登录身份
	private long? _loginConnection;

	public LiveDanmakuViewModel(IDanmakuFeed feed, DanmakuFilterViewModel filter, bool autoRefresh = true)
	{
		_feed = feed.DisposeWith(Disposables);
		Filter = filter;
		Filter.CriteriaChanged.Subscribe(_ => Rebuild()).DisposeWith(Disposables);
		ScrollToLatestCommand.DisposeWith(Disposables);

		this.WhenChanged(static viewModel => viewModel.IsFollowing)
			.Keep(static isFollowing => isFollowing)
			.Subscribe
			(_ =>
				{
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

		Refresh();
		OnLanguageChanged(UpdateTexts);

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

	public RangeObservableCollection<DanmakuEntryViewModel> Entries { get; } = [];

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
	[MemberNotNull(nameof(_status))]
	internal void Refresh()
	{
		long version = _feed.Version;

		if (version != _lastVersion)
		{
			_lastVersion = version;
			Append(_feed.GetEntries(_lastSequence));
		}

		_status = _feed.Status;
		UpdateStatus();
	}

	// 连接换掉之前状态里还是匿名，先显示正在等重连
	internal void OnLoggedIn()
	{
		_loginConnection = _status.Connection;
		UpdateStatus();
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
			_lastSequence = entry.Sequence;

			if (entry.Entry is DanmakuEntryDto.SuperChatDeletion deletion)
			{
				// 不跟随时列表可能还显示着已经挤出 _all 的 SC
				DanmakuEntryViewModel.MarkDeleted(_isStale ? _all.Union(Entries) : _all, deletion.Ids);
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

		Entries.RemoveFirst(Entries.LowerBound(entry => entry.Sequence <= lastRemoved));
	}

	// 重建后原来的滚动位置没有意义
	private void Rebuild()
	{
		Reset();
		IsFollowing = true;
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

	private void UpdateStatus()
	{
		State = _status.State;
		StateText = _status.State switch
		{
			DanmakuConnectionState.Connecting => AppLanguage.Current.DanmakuConnecting,
			DanmakuConnectionState.Connected => AppLanguage.Current.DanmakuConnected,
			DanmakuConnectionState.Disconnected => AppLanguage.Current.DanmakuDisconnected
		};
		(IdentityText, CanLogin) = _status switch
		{
			{ UserId: null } => ((string?)null, false),
			{ UserId: > 0 } => (AppLanguage.Current.DanmakuLoggedIn, false),
			{ IsMasked: false } => (AppLanguage.Current.DanmakuAnonymous, false),
			_ when _status.Connection == _loginConnection => (AppLanguage.Current.DanmakuLoginPending, false),
			_ => (AppLanguage.Current.DanmakuAnonymousMasked, true)
		};
		RateText = AppLanguage.Current.DanmakuRate.ToString(_status.MessagesPerMinute.ToString(CultureInfo.CurrentCulture));
		ReconnectingText = _status.DisconnectedAt is { } disconnectedAt ? AppLanguage.Current.DanmakuReconnecting.ToString(disconnectedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)) : null;
	}

	private void UpdateNewEntriesText()
	{
		NewEntriesText = NewEntryCount is 0 ? null : AppLanguage.Current.NewDanmakuCount.ToString(NewEntryCount.ToString(CultureInfo.CurrentCulture));
	}

	private void UpdateTexts()
	{
		UpdateStatus();
		UpdateNewEntriesText();
	}
}
