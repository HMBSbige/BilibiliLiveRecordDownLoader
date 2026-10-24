namespace BilibiliLiveRecordDownLoader.Domain.Shared;

public enum BackgroundTaskStatus
{
	Queued,
	Running,
	Completed,
	Failed,
	Canceled
}
