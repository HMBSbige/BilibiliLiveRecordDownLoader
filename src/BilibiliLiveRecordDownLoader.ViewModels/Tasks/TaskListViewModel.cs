namespace BilibiliLiveRecordDownLoader.ViewModels.Tasks;

public sealed partial class TaskListViewModel : ViewModelBase
{
	private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(300);

	private readonly IBackgroundTaskAppService _taskAppService;
	private readonly DialogHostViewModel _dialogs;
	private readonly ILauncherService _launcher;

	public TaskListViewModel(IBackgroundTaskAppService taskAppService, DialogHostViewModel dialogs, ILauncherService launcher)
	{
		_taskAppService = taskAppService;
		_dialogs = dialogs;
		_launcher = launcher;
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

	internal void Refresh()
	{
		IReadOnlyList<BackgroundTaskSnapshot> snapshots = _taskAppService.GetTasks();
		HashSet<Guid> ids = [.. snapshots.Select(static snapshot => snapshot.Id)];

		for (int index = Tasks.Count - 1; index >= 0; --index)
		{
			if (!ids.Contains(Tasks[index].Id))
			{
				Tasks.RemoveAt(index);
			}
		}

		Dictionary<Guid, TaskItemViewModel> existing = Tasks.ToDictionary(static task => task.Id);

		foreach (BackgroundTaskSnapshot snapshot in snapshots)
		{
			if (existing.TryGetValue(snapshot.Id, out TaskItemViewModel? task))
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
			await _launcher.OpenFolderAsync(directory);
		}
	}

	[ReactiveCommand(CanExecute = nameof(CanCancelAll))]
	private async Task CancelAllTasksAsync()
	{
		if (_taskAppService.HasActiveTasks && await _dialogs.ConfirmAsync(AppLanguage.Current.CancelAllTasks, AppLanguage.Current.CancelAllTasksConfirm, AppLanguage.Current.CancelAllTasks, true))
		{
			_taskAppService.CancelAll();
		}
	}

	[ReactiveCommand(CanExecute = nameof(CanClearFinished))]
	private void ClearFinishedTasks()
	{
		_taskAppService.RemoveFinished();
	}
}
