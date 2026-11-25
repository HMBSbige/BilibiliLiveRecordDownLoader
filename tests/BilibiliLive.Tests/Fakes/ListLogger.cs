using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BilibiliLive.Tests.Fakes;

internal sealed record LogEntry(LogLevel Level, string Message);

internal sealed class ListLogger : ILogger
{
	public ConcurrentQueue<LogEntry> Entries { get; } = new();

	public IDisposable? BeginScope<TState>(TState state) where TState : notnull
	{
		return null;
	}

	public bool IsEnabled(LogLevel logLevel)
	{
		return true;
	}

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
	{
		Entries.Enqueue(new LogEntry(logLevel, formatter(state, exception)));
	}
}
