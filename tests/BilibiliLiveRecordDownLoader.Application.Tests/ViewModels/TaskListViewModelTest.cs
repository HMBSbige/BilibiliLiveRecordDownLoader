using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.ViewModels.Dialogs;
using BilibiliLiveRecordDownLoader.ViewModels.Tasks;
using ReactiveUI.Primitives;
using System.Windows.Input;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class TaskListViewModelTest
{
	[Test]
	public async Task TasksFollowTheSnapshotsAsync()
	{
		BackgroundTaskSnapshot running = Snapshot("a", BackgroundTaskStatus.Running);
		BackgroundTaskSnapshot queued = Snapshot("b", BackgroundTaskStatus.Queued);
		BackgroundTaskSnapshot added = Snapshot("c", BackgroundTaskStatus.Queued);
		FakeTasks tasks = new([running, queued]);
		using TaskListViewModel viewModel = new(tasks, new DialogHostViewModel(), new PlatformStub());
		viewModel.Refresh();
		TaskItemViewModel first = viewModel.Tasks[0];
		bool wasActive = viewModel.HasActiveTasks;

		tasks.Set([running with { Status = BackgroundTaskStatus.Completed, Progress = 1 }, added]);
		viewModel.Refresh();

		using (Assert.Multiple())
		{
			await Assert.That(wasActive).IsTrue();
			await Assert.That(viewModel.Tasks.Select(static task => task.Title)).IsEquivalentTo(["a", "c"], CollectionOrdering.Matching);
			await Assert.That(viewModel.Tasks[0]).IsSameReferenceAs(first);
			await Assert.That(first.Status).IsEqualTo(BackgroundTaskStatus.Completed);
			await Assert.That((viewModel.HasTasks, viewModel.HasActiveTasks, viewModel.HasFinishedTasks)).IsEqualTo((true, true, true));
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task CancellingAllTasksWaitsForTheConfirmationAsync(bool confirmed, CancellationToken cancellationToken)
	{
		FakeTasks tasks = new([Snapshot("a", BackgroundTaskStatus.Running)]) { HasActiveTasks = true };
		using DialogHostViewModel dialogs = new();
		using TaskListViewModel viewModel = new(tasks, dialogs, new PlatformStub());
		viewModel.Refresh();
		Task<DialogViewModel?> confirmation = dialogs.WhenChanged(static dialogs => dialogs.Current).Keep(static dialog => dialog is ConfirmDialogViewModel).FirstAsync(cancellationToken);

		Task cancelling = viewModel.CancelAllTasksCommand.Execute().ToTask(cancellationToken);
		DialogViewModel? dialog = await confirmation;
		int cancelledBeforeAnswer = tasks.CancelAllCount;

		if (confirmed && dialog is ConfirmDialogViewModel confirm)
		{
			((ICommand)confirm.ConfirmCommand).Execute(null);
		}
		else
		{
			dialog?.Dismiss();
		}

		await cancelling;

		await Assert.That((cancelledBeforeAnswer, tasks.CancelAllCount)).IsEqualTo((0, confirmed ? 1 : 0));
	}

	[Test]
	public async Task OpeningTheTargetFolderUsesTheDirectoryOfTheTargetAsync(CancellationToken cancellationToken)
	{
		string target = Path.Combine("downloads", "video.mp4");
		FakeTasks tasks = new([Snapshot("a", BackgroundTaskStatus.Completed) with { TargetPath = target }]);
		PlatformStub platform = new();
		using TaskListViewModel viewModel = new(tasks, new DialogHostViewModel(), platform);
		viewModel.Refresh();

		await viewModel.OpenTargetFolderCommand.Execute(viewModel.Tasks[0]).ToTask(cancellationToken);

		await Assert.That(platform.OpenedFolders).IsEquivalentTo(["downloads"]);
	}

	private static BackgroundTaskSnapshot Snapshot(string title, BackgroundTaskStatus status)
	{
		return new BackgroundTaskSnapshot(Guid.NewGuid(), title, null, null, status, null, null, null);
	}

	private sealed class FakeTasks(IReadOnlyList<BackgroundTaskSnapshot> initial) : IBackgroundTaskAppService
	{
		private IReadOnlyList<BackgroundTaskSnapshot> _tasks = initial;
		private long _version;
		private int _cancelAllCount;

		public long Version => Volatile.Read(ref _version);

		public bool HasActiveTasks { get; init; }

		public int CancelAllCount => Volatile.Read(ref _cancelAllCount);

		public void Set(IReadOnlyList<BackgroundTaskSnapshot> tasks)
		{
			Volatile.Write(ref _tasks, tasks);
			Interlocked.Increment(ref _version);
		}

		public IReadOnlyList<BackgroundTaskSnapshot> GetTasks()
		{
			return Volatile.Read(ref _tasks);
		}

		public void Cancel(Guid id)
		{
		}

		public void CancelAll()
		{
			Interlocked.Increment(ref _cancelAllCount);
		}

		public void RemoveFinished()
		{
		}
	}
}
