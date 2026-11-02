namespace BilibiliLiveRecordDownLoader.Application.Contracts.Logging;

public interface ILogStore
{
	long Version { get; }

	IReadOnlyList<LogEntry> GetEntries();

	void Add(DateTimeOffset timestamp, LogLevel level, long? roomId, string message);
}
