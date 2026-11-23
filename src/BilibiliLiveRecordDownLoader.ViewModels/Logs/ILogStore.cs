namespace BilibiliLiveRecordDownLoader.ViewModels.Logs;

public interface ILogStore
{
	long Version { get; }

	IReadOnlyList<LogEntry> GetEntries();
}
