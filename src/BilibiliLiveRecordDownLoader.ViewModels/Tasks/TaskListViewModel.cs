namespace BilibiliLiveRecordDownLoader.ViewModels.Tasks;

public sealed partial class TaskListViewModel : ViewModelBase
{
	private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(300);

	private readonly IBackgroundTaskAppService _taskAppService;

	public TaskListViewModel(IBackgroundTaskAppService taskAppService)
	{
		_taskAppService = taskAppService;
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
		PollChanges(() => _taskAppService.Version, Refresh, RefreshInterval);
	}

	// 测试不启动定时刷新，改为直接调用
	internal void Refresh()
	{
		Sync(_taskAppService.GetTasks());
	}

	[ReactiveCommand]
	private void CancelTask(TaskItemViewModel task)
	{
		_taskAppService.Cancel(task.Id);
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
		if (_taskAppService.HasActiveTasks && await AppLocator.Current.GetRequiredService<DialogHostViewModel>().ConfirmAsync(AppLanguage.Current.CancelAllTasks, AppLanguage.Current.CancelAllTasksConfirm, AppLanguage.Current.CancelAllTasks, true))
		{
			_taskAppService.CancelAll();
		}
	}

	[ReactiveCommand(CanExecute = nameof(CanClearFinished))]
	private void ClearFinishedTasks()
	{
		_taskAppService.RemoveFinished();
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
