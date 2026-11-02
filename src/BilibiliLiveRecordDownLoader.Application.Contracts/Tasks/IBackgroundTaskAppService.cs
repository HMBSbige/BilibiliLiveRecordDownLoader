namespace BilibiliLiveRecordDownLoader.Application.Contracts.Tasks;

public interface IBackgroundTaskAppService
{
	long Version { get; }

	bool HasActiveTasks { get; }

	IReadOnlyList<BackgroundTaskSnapshot> GetTasks();

	void Cancel(Guid id);

	void CancelAll();

	void RemoveFinished();
}
