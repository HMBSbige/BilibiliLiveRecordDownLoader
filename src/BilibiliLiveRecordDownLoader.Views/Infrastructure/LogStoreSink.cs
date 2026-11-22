using System.Collections.Concurrent;

namespace BilibiliLiveRecordDownLoader.Views.Infrastructure;

// 日志管道在依赖注入容器之前创建，容器就绪前的日志先暂存
internal sealed class LogStoreSink : ILogEventSink
{
	private const int PendingCapacity = 256;

	private readonly ConcurrentQueue<LogEvent> _pending = new();
	private volatile ILogStore? _store;

	public void Attach(ILogStore store)
	{
		_store = store;

		while (_pending.TryDequeue(out LogEvent? logEvent))
		{
			Write(store, logEvent);
		}
	}

	public void Emit(LogEvent logEvent)
	{
		if (_store is { } store)
		{
			Write(store, logEvent);
			return;
		}

		_pending.Enqueue(logEvent);

		while (_pending.Count > PendingCapacity)
		{
			_pending.TryDequeue(out _);
		}
	}

	private static void Write(ILogStore store, LogEvent logEvent)
	{
		long? roomId = logEvent.Properties.TryGetValue(LogProperties.RoomId, out LogEventPropertyValue? value) && value is ScalarValue { Value: long id } ? id : null;
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

		store.Add(logEvent.Timestamp, level, roomId, message);
	}
}
