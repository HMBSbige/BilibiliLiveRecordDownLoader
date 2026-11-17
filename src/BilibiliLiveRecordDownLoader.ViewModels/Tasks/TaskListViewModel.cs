namespace BilibiliLiveRecordDownLoader.ViewModels.Tasks;

public sealed partial class TaskListViewModel : ViewModelBase
{
	private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(300);

	public TaskListViewModel()
	{
		CancelTaskCommand.DisposeWith(Disposables);
		CancelAllTasksCommand.DisposeWith(Disposables);
		ClearFinishedTasksCommand.DisposeWith(Disposables);
	}

	public ObservableCollection<TaskItemViewModel> Tasks { get; } = [];

	[Reactive]
	public partial bool HasTasks { get; private set; }

	internal void StartMonitoring()
	{
		IBackgroundTaskAppService taskAppService = AppLocator.Current.GetRequiredService<IBackgroundTaskAppService>();
		PollChanges(() => taskAppService.Version, () => Sync(taskAppService.GetTasks()), RefreshInterval);
	}

	[ReactiveCommand]
	private void CancelTask(TaskItemViewModel task)
	{
		AppLocator.Current.GetRequiredService<IBackgroundTaskAppService>().Cancel(task.Id);
	}

	[ReactiveCommand]
	private async Task CancelAllTasksAsync()
	{
		IBackgroundTaskAppService taskAppService = AppLocator.Current.GetRequiredService<IBackgroundTaskAppService>();

		if (taskAppService.HasActiveTasks && await AppLocator.Current.GetRequiredService<DialogHostViewModel>().ConfirmAsync(AppLanguage.Current.CancelAllTasks, AppLanguage.Current.CancelAllTasksConfirm))
		{
			taskAppService.CancelAll();
		}
	}

	[ReactiveCommand]
	private void ClearFinishedTasks()
	{
		AppLocator.Current.GetRequiredService<IBackgroundTaskAppService>().RemoveFinished();
	}

	private void Sync(IReadOnlyList<BackgroundTaskSnapshot> snapshots)
	{
		HashSet<Guid> ids = [.. snapshots.Select(static snapshot => snapshot.Id)];

		for (int index = Tasks.Count - 1; index >= 0; --index)
		{
			if (!ids.Contains(Tasks[index].Id))
			{
				Tasks.RemoveAt(index);
			}
		}

		foreach (BackgroundTaskSnapshot snapshot in snapshots)
		{
			if (Tasks.FirstOrDefault(task => task.Id == snapshot.Id) is { } task)
			{
				task.Apply(snapshot);
			}
			else
			{
				Tasks.Add(new TaskItemViewModel(snapshot));
			}
		}

		HasTasks = Tasks.Count > 0;
	}
}
