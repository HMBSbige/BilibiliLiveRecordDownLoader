namespace BilibiliLiveRecordDownLoader.Application.Contracts.Logging;

public interface ILogStore
{
	long Version { get; }

	IReadOnlyList<LogEntry> GetEntries();
}
