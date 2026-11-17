namespace BilibiliLiveRecordDownLoader.ViewModels.Tasks;

public sealed partial class TaskItemViewModel : ReactiveObject
{
	public TaskItemViewModel(BackgroundTaskSnapshot snapshot)
	{
		Id = snapshot.Id;
		Title = snapshot.Title;
		Apply(snapshot);
	}

	public Guid Id { get; }

	public string Title { get; }

	[Reactive]
	public partial BackgroundTaskStatus Status { get; private set; }

	[Reactive]
	public partial double Progress { get; private set; }

	[Reactive]
	public partial bool IsIndeterminate { get; private set; }

	[Reactive]
	public partial bool IsActive { get; private set; }

	[Reactive]
	public partial string? Message { get; private set; }

	internal void Apply(BackgroundTaskSnapshot snapshot)
	{
		Status = snapshot.Status;
		Progress = (snapshot.Progress ?? 0) * 100;
		IsActive = snapshot.Status is BackgroundTaskStatus.Queued or BackgroundTaskStatus.Running;
		IsIndeterminate = snapshot is { Status: BackgroundTaskStatus.Running, Progress: null };
		Message = snapshot.Message;
	}
}
