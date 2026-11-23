namespace BilibiliLiveRecordDownLoader.Application.Contracts.Tasks;

public enum BackgroundTaskStatus
{
	Queued,
	Running,
	Completed,
	Failed,
	Canceled
}
