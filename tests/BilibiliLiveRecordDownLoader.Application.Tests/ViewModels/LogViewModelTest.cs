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
		using LogViewModel viewModel = new(store);

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
		using LogViewModel viewModel = new(store);
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
