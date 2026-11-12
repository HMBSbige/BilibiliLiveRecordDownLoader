namespace BilibiliLiveRecordDownLoader.Application.Logging;

internal sealed class LogStore : ILogStore
{
	private const int Capacity = 1000;

	private readonly Lock _lock = new();
	private readonly Queue<LogEntry> _entries = new(Capacity);
	private long _sequence;

	public long Version => Interlocked.Read(ref _sequence);

	public IReadOnlyList<LogEntry> GetEntries()
	{
		lock (_lock)
		{
			return [.. _entries];
		}
	}

	public void Add(DateTimeOffset timestamp, LogLevel level, long? roomId, string message)
	{
		lock (_lock)
		{
			if (_entries.Count is Capacity)
			{
				_entries.Dequeue();
			}

			_entries.Enqueue(new LogEntry(++_sequence, timestamp, level, roomId, message));
		}
	}
}
