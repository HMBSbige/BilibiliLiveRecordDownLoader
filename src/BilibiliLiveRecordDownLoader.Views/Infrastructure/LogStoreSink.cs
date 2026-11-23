namespace BilibiliLiveRecordDownLoader.Views.Infrastructure;

// 日志管道早于容器创建，由 sink 自己保存最近的日志，容器建好之前的也不会丢
internal sealed class LogStoreSink : ILogEventSink, ILogStore
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

	public void Emit(LogEvent logEvent)
	{
		long? roomId = logEvent.Properties.GetValueOrDefault(LogProperties.RoomId) is ScalarValue { Value: long id } ? id : null;
		string message = logEvent.RenderMessage(CultureInfo.CurrentCulture);

		if (logEvent.Exception is { } exception)
		{
			message = $"{message}{Environment.NewLine}{exception.Message}";
		}

		LogLevel level = logEvent.Level switch
		{
			LogEventLevel.Verbose => LogLevel.Trace,
			LogEventLevel.Debug => LogLevel.Debug,
			LogEventLevel.Information => LogLevel.Information,
			LogEventLevel.Warning => LogLevel.Warning,
			LogEventLevel.Error => LogLevel.Error,
			LogEventLevel.Fatal => LogLevel.Critical
		};

		lock (_lock)
		{
			if (_entries.Count is Capacity)
			{
				_entries.Dequeue();
			}

			_entries.Enqueue(new LogEntry(++_sequence, logEvent.Timestamp, level, roomId, message));
		}
	}
}
