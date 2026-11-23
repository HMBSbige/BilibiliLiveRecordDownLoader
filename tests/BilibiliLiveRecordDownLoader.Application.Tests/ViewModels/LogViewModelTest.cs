using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.ViewModels.Logs;
using System.Windows.Input;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class LogViewModelTest
{
	private static readonly LogLevel[] Levels = [LogLevel.Information, LogLevel.Error, LogLevel.Warning];

	[Test]
	[Arguments(LogSortColumn.Time)]
	[Arguments(LogSortColumn.Level)]
	[Arguments(LogSortColumn.Room)]
	public async Task EntriesArrivingLaterAreInsertedInSortedOrderAsync(LogSortColumn column)
	{
		FakeLogStore store = new();
		using LogViewModel viewModel = new(store, new PlatformStub());

		if (column is not LogSortColumn.Time)
		{
			((ICommand)viewModel.SortByCommand).Execute(column);
		}

		AddEntries(store, 0, 7);
		viewModel.Refresh();
		AddEntries(store, 7, 13);
		viewModel.Refresh();

		IEnumerable<LogEntry> ordered = column switch
		{
			LogSortColumn.Time => store.GetEntries().OrderBy(static entry => entry.Sequence),
			LogSortColumn.Level => store.GetEntries().OrderBy(static entry => entry.Level).ThenBy(static entry => entry.Sequence),
			LogSortColumn.Room => store.GetEntries().OrderBy(static entry => entry.RoomId).ThenBy(static entry => entry.Sequence)
		};
		long[] expected = [.. (viewModel.Sort.IsDescending ? ordered.Reverse() : ordered).Select(static entry => entry.Sequence)];

		await Assert.That(viewModel.Entries.Select(static entry => entry.Sequence)).IsEquivalentTo(expected, CollectionOrdering.Matching);
	}

	[Test]
	public async Task OnlyTheNewestEntriesAreKeptAsync()
	{
		FakeLogStore store = new();
		using LogViewModel viewModel = new(store, new PlatformStub());
		AddEntries(store, 0, 1000);
		viewModel.Refresh();

		AddEntries(store, 1000, 5);
		viewModel.Refresh();

		using (Assert.Multiple())
		{
			await Assert.That(viewModel.Entries).Count().IsEqualTo(1000);
			await Assert.That((viewModel.Entries.Min(static entry => entry.Sequence), viewModel.Entries.Max(static entry => entry.Sequence))).IsEqualTo((6L, 1005L));
		}
	}

	[Test]
	public async Task ClickingTheSortedColumnAgainReversesItAsync()
	{
		using LogViewModel viewModel = new(new FakeLogStore(), new PlatformStub());
		List<LogSort> sorts = [viewModel.Sort];

		foreach (LogSortColumn column in (LogSortColumn[])[LogSortColumn.Time, LogSortColumn.Room, LogSortColumn.Room, LogSortColumn.Level])
		{
			((ICommand)viewModel.SortByCommand).Execute(column);
			sorts.Add(viewModel.Sort);
		}

		await Assert.That(sorts).IsEquivalentTo([new LogSort(LogSortColumn.Time, true), new LogSort(LogSortColumn.Time, false), new LogSort(LogSortColumn.Room, false), new LogSort(LogSortColumn.Room, true), new LogSort(LogSortColumn.Level, true)], CollectionOrdering.Matching);
	}

	public static IEnumerable<Func<(LogLevel? Minimum, string? Room, string? Search, long[] Expected)>> Filters()
	{
		yield return static () => (LogLevel.Warning, null, null, [2, 3, 4]);
		yield return static () => (null, " 2 ", null, [1, 4]);
		yield return static () => (null, null, "RECORDING", [1, 4]);
		yield return static () => (LogLevel.Warning, "2", "recording", [4]);
	}

	[Test]
	[MethodDataSource(nameof(Filters))]
	public async Task OnlyEntriesMatchingEveryFilterAreShownUntilTheFiltersAreResetAsync(LogLevel? minimum, string? room, string? search, long[] expected)
	{
		FakeLogStore store = new();
		store.Add(DateTimeOffset.UnixEpoch, LogLevel.Information, 12, "Recording started");
		store.Add(DateTimeOffset.UnixEpoch, LogLevel.Warning, 3, "Reconnecting");
		store.Add(DateTimeOffset.UnixEpoch, LogLevel.Error, null, "Disk full");
		store.Add(DateTimeOffset.UnixEpoch, LogLevel.Error, 21, "Recording failed");
		using LogViewModel viewModel = new(store, new PlatformStub());

		viewModel.MinimumLevel = minimum is { } level ? new LogLevelOption(level) : LogLevelOption.All;
		viewModel.RoomFilter = room;
		viewModel.SearchText = search;
		long[] filtered = Matching(store, viewModel);
		((ICommand)viewModel.ResetFiltersCommand).Execute(null);

		using (Assert.Multiple())
		{
			await Assert.That(filtered).IsEquivalentTo(expected, CollectionOrdering.Matching);
			await Assert.That(Matching(store, viewModel)).IsEquivalentTo([1L, 2L, 3L, 4L], CollectionOrdering.Matching);
		}
	}

	private static long[] Matching(FakeLogStore store, LogViewModel viewModel)
	{
		return [.. store.GetEntries().Where(viewModel.CreatePredicate()).Select(static entry => entry.Sequence)];
	}

	private static void AddEntries(FakeLogStore store, int start, int count)
	{
		for (int index = start; index < start + count; ++index)
		{
			store.Add(DateTimeOffset.UnixEpoch.AddSeconds(index), Levels[index % Levels.Length], index % 4 is 0 ? null : index % 3, $"entry {index}");
		}
	}

	private sealed class FakeLogStore : ILogStore
	{
		private readonly List<LogEntry> _entries = [];

		public long Version => _entries.Count;

		public IReadOnlyList<LogEntry> GetEntries()
		{
			return [.. _entries];
		}

		public void Add(DateTimeOffset timestamp, LogLevel level, long? roomId, string message)
		{
			_entries.Add(new LogEntry(_entries.Count + 1, timestamp, level, roomId, message));
		}
	}
}
