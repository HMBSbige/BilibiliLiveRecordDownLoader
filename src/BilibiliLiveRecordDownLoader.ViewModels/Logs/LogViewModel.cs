using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace BilibiliLiveRecordDownLoader.ViewModels.Logs;

public sealed partial class LogViewModel : ViewModelBase
{
	private const int Capacity = 1000;
	private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(500);
	private static readonly TimeSpan FilterDelay = TimeSpan.FromMilliseconds(200);

	private readonly List<LogEntry> _all = [];
	private readonly IComparer<LogEntry> _comparer;
	private long _lastSequence;

	public LogViewModel()
	{
		_comparer = Comparer<LogEntry>.Create(Compare);

		CanResetFilters = this.WhenChanged
		(
			static viewModel => viewModel.MinimumLevel,
			static viewModel => viewModel.RoomFilter,
			static viewModel => viewModel.SearchText,
			static (level, room, search) => level != ChoiceOption.Inherit || !string.IsNullOrWhiteSpace(room) || !string.IsNullOrWhiteSpace(search)
		);

		// 输入框停顿后再过滤，级别和排序立即生效
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

	// 按当前过滤和排序显示的日志
	public ObservableCollection<LogEntry> Entries { get; } = [];

	public IReadOnlyList<ChoiceOption> LevelOptions { get; } = [ChoiceOption.Inherit, .. ChoiceOption.Of(LogLevel.Information, LogLevel.Warning, LogLevel.Error)];

	public IObservable<bool> CanResetFilters { get; }

	[Reactive]
	public partial ChoiceOption MinimumLevel { get; set; } = ChoiceOption.Inherit;

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
		ILogStore logStore = AppLocator.Current.GetRequiredService<ILogStore>();
		PollChanges(() => logStore.Version, () => Append(logStore.GetEntries()), RefreshInterval);
	}

	[ReactiveCommand]
	private async Task OpenLogFolderAsync()
	{
		await AppLocator.Current.GetRequiredService<ILauncherService>().OpenFolderAsync(ConfigurationConsts.LogDirectory);
	}

	[ReactiveCommand(CanExecute = nameof(CanResetFilters))]
	private void ResetFilters()
	{
		MinimumLevel = ChoiceOption.Inherit;
		RoomFilter = null;
		SearchText = null;
	}

	// 再次点击同一列切换方向，时间和级别默认降序
	[ReactiveCommand]
	private void SortBy(LogSortColumn column)
	{
		Sort = new LogSort(column, column == Sort.Column ? !Sort.IsDescending : column is not LogSortColumn.Room);
	}

	private void Append(IReadOnlyList<LogEntry> entries)
	{
		foreach (LogEntry entry in entries.Where(entry => entry.Sequence > _lastSequence))
		{
			_all.Add(entry);
			_lastSequence = entry.Sequence;

			if (Matches(entry))
			{
				int index = BinarySearch(entry);
				Entries.Insert(index < 0 ? ~index : index, entry);
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

	private void Rebuild()
	{
		Entries.Clear();

		foreach (LogEntry entry in _all.Where(Matches).Order(_comparer))
		{
			Entries.Add(entry);
		}

		UpdateState();
	}

	private void UpdateState()
	{
		HasEntries = _all.Count > 0;
		HasVisibleEntries = Entries.Count > 0;
	}

	private bool Matches(LogEntry entry)
	{
		if (MinimumLevel.As<LogLevel>() is { } level && entry.Level < level)
		{
			return false;
		}

		if (!string.IsNullOrWhiteSpace(RoomFilter) && !(entry.RoomId?.ToString(CultureInfo.InvariantCulture).Contains(RoomFilter.Trim(), StringComparison.Ordinal) ?? false))
		{
			return false;
		}

		return string.IsNullOrWhiteSpace(SearchText) || entry.Message.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase);
	}

	private int BinarySearch(LogEntry entry)
	{
		int low = 0;
		int high = Entries.Count - 1;

		while (low <= high)
		{
			int middle = low + (high - low) / 2;
			int result = _comparer.Compare(Entries[middle], entry);

			if (result is 0)
			{
				return middle;
			}

			if (result < 0)
			{
				low = middle + 1;
			}
			else
			{
				high = middle - 1;
			}
		}

		return ~low;
	}

	private int Compare(LogEntry x, LogEntry y)
	{
		int result = Sort.Column switch
		{
			LogSortColumn.Level => x.Level.CompareTo(y.Level),
			LogSortColumn.Room => Nullable.Compare(x.RoomId, y.RoomId),
			_ => 0
		};

		if (result is 0)
		{
			result = x.Sequence.CompareTo(y.Sequence);
		}

		return Sort.IsDescending ? -result : result;
	}
}
