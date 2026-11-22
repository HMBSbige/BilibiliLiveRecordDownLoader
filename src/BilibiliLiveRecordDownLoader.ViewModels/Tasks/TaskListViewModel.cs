namespace BilibiliLiveRecordDownLoader.ViewModels.Tasks;

public sealed partial class TaskListViewModel : ViewModelBase
{
	private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(300);

	public TaskListViewModel()
	{
		CanCancelAll = this.WhenChanged(static viewModel => viewModel.HasActiveTasks);
		CanClearFinished = this.WhenChanged(static viewModel => viewModel.HasFinishedTasks);

		CancelTaskCommand.DisposeWith(Disposables);
		OpenTargetFolderCommand.DisposeWith(Disposables);
		CancelAllTasksCommand.DisposeWith(Disposables);
		ClearFinishedTasksCommand.DisposeWith(Disposables);
	}

	public ObservableCollection<TaskItemViewModel> Tasks { get; } = [];

	public IObservable<bool> CanCancelAll { get; }

	public IObservable<bool> CanClearFinished { get; }

	[Reactive]
	public partial bool HasTasks { get; private set; }

	[Reactive]
	public partial bool HasActiveTasks { get; private set; }

	[Reactive]
	public partial bool HasFinishedTasks { get; private set; }

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
	private async Task OpenTargetFolderAsync(TaskItemViewModel task)
	{
		if (Path.GetDirectoryName(task.TargetPath) is { Length: > 0 } directory)
		{
			await AppLocator.Current.GetRequiredService<ILauncherService>().OpenFolderAsync(directory);
		}
	}

	[ReactiveCommand(CanExecute = nameof(CanCancelAll))]
	private async Task CancelAllTasksAsync()
	{
		IBackgroundTaskAppService taskAppService = AppLocator.Current.GetRequiredService<IBackgroundTaskAppService>();

		if (taskAppService.HasActiveTasks && await AppLocator.Current.GetRequiredService<DialogHostViewModel>().ConfirmAsync(AppLanguage.Current.CancelAllTasks, AppLanguage.Current.CancelAllTasksConfirm, AppLanguage.Current.CancelAllTasks, true))
		{
			taskAppService.CancelAll();
		}
	}

	[ReactiveCommand(CanExecute = nameof(CanClearFinished))]
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
		HasActiveTasks = Tasks.Any(static task => task.IsActive);
		HasFinishedTasks = Tasks.Any(static task => !task.IsActive);
	}
}
