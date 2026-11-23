using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace BilibiliLiveRecordDownLoader.ViewModels.Logs;

public sealed partial class LogViewModel : ViewModelBase
{
	private const int Capacity = 1000;
	private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(500);
	private static readonly TimeSpan FilterDelay = TimeSpan.FromMilliseconds(200);

	private readonly ILogStore _logStore;
	private readonly ILauncherService _launcher;
	private readonly List<LogEntry> _all = [];
	private readonly Comparer<LogEntry> _comparer;
	private long _lastSequence;

	public LogViewModel(ILogStore logStore, ILauncherService launcher)
	{
		_logStore = logStore;
		_launcher = launcher;
		_comparer = Comparer<LogEntry>.Create(Compare);

		CanResetFilters = this.WhenChanged
		(
			static viewModel => viewModel.MinimumLevel,
			static viewModel => viewModel.RoomFilter,
			static viewModel => viewModel.SearchText,
			static (level, room, search) => level != LogLevelOption.All || !string.IsNullOrWhiteSpace(room) || !string.IsNullOrWhiteSpace(search)
		);

		Signal.Merge
			(
				this.WhenChanged(static viewModel => viewModel.MinimumLevel).Skip(1).Map(static _ => RxVoid.Default),
				this.WhenChanged(static viewModel => viewModel.Sort).Skip(1).Map(static _ => RxVoid.Default),
				this.WhenChanged(static viewModel => viewModel.RoomFilter, static viewModel => viewModel.SearchText, static (_, _) => RxVoid.Default)
					.Skip(1)
					.Calm(FilterDelay, RxSchedulers.TaskpoolScheduler)
					.ObserveOn(RxSchedulers.MainThreadScheduler)
			)
			.Subscribe(_ => Rebuild())
			.DisposeWith(Disposables);

		OpenLogFolderCommand.DisposeWith(Disposables);
		ResetFiltersCommand.DisposeWith(Disposables);
		SortByCommand.DisposeWith(Disposables);
	}

	public RangeObservableCollection<LogEntry> Entries { get; } = [];

	public IReadOnlyList<LogLevelOption> LevelOptions { get; } = [LogLevelOption.All, new(LogLevel.Information), new(LogLevel.Warning), new(LogLevel.Error)];

	public IObservable<bool> CanResetFilters { get; }

	[Reactive]
	public partial LogLevelOption MinimumLevel { get; set; } = LogLevelOption.All;

	[Reactive]
	public partial string? RoomFilter { get; set; }

	[Reactive]
	public partial string? SearchText { get; set; }

	[Reactive]
	public partial LogSort Sort { get; private set; } = new(LogSortColumn.Time, true);

	[Reactive]
	public partial bool HasEntries { get; private set; }

	[Reactive]
	public partial bool HasVisibleEntries { get; private set; }

	internal void StartMonitoring()
	{
		PollChanges(() => _logStore.Version, Refresh, RefreshInterval);
	}

	internal void Refresh()
	{
		Func<LogEntry, bool> matches = CreatePredicate();

		foreach (LogEntry entry in _logStore.GetEntries().Where(entry => entry.Sequence > _lastSequence))
		{
			_all.Add(entry);
			_lastSequence = entry.Sequence;

			if (matches(entry))
			{
				// 比较器最后按序号比，新条目不会和已有的相等
				Entries.Insert(Entries.LowerBound(shown => _comparer.Compare(shown, entry) < 0), entry);
			}
		}

		if (_all.Count > Capacity)
		{
			foreach (LogEntry removed in _all.Take(_all.Count - Capacity))
			{
				Entries.Remove(removed);
			}

			_all.RemoveRange(0, _all.Count - Capacity);
		}

		UpdateState();
	}

	[ReactiveCommand]
	private async Task OpenLogFolderAsync()
	{
		await _launcher.OpenFolderAsync(ConfigurationConsts.LogDirectory);
	}

	[ReactiveCommand(CanExecute = nameof(CanResetFilters))]
	private void ResetFilters()
	{
		MinimumLevel = LogLevelOption.All;
		RoomFilter = null;
		SearchText = null;
	}

	[ReactiveCommand]
	private void SortBy(LogSortColumn column)
	{
		Sort = new LogSort(column, column == Sort.Column ? !Sort.IsDescending : column is not LogSortColumn.Room);
	}

	internal Func<LogEntry, bool> CreatePredicate()
	{
		LogLevel minimum = MinimumLevel.Minimum ?? LogLevel.Trace;
		string? room = string.IsNullOrWhiteSpace(RoomFilter) ? null : RoomFilter.Trim();
		string? search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();

		// 过滤在界面线程上逐条进行，按区域性比较中文会慢很多
		return entry => entry.Level >= minimum
			&& (room is null || entry.RoomId is { } roomId && roomId.ToString(CultureInfo.InvariantCulture).Contains(room, StringComparison.Ordinal))
			&& (search is null || entry.Message.Contains(search, StringComparison.OrdinalIgnoreCase));
	}

	private void Rebuild()
	{
		Entries.Reset(_all.Where(CreatePredicate()).Order(_comparer));
		UpdateState();
	}

	private void UpdateState()
	{
		HasEntries = _all.Count > 0;
		HasVisibleEntries = Entries.Count > 0;
	}

	private int Compare(LogEntry x, LogEntry y)
	{
		// 按时间排就是按序号排，交给下面统一比较
		int result = Sort.Column switch
		{
			LogSortColumn.Time => 0,
			LogSortColumn.Level => x.Level.CompareTo(y.Level),
			LogSortColumn.Room => Nullable.Compare(x.RoomId, y.RoomId)
		};

		if (result is 0)
		{
			result = x.Sequence.CompareTo(y.Sequence);
		}

		return Sort.IsDescending ? -result : result;
	}
}
