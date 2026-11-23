using BilibiliLiveRecordDownLoader.ViewModels.Tasks;

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
		using TaskListViewModel viewModel = new(tasks);
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

	private static BackgroundTaskSnapshot Snapshot(string title, BackgroundTaskStatus status)
	{
		return new BackgroundTaskSnapshot(Guid.NewGuid(), title, null, null, status, null, null, null);
	}

	private sealed class FakeTasks(IReadOnlyList<BackgroundTaskSnapshot> initial) : IBackgroundTaskAppService
	{
		private IReadOnlyList<BackgroundTaskSnapshot> _tasks = initial;
		private long _version;

		public long Version => Volatile.Read(ref _version);

		public bool HasActiveTasks => false;

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
		}

		public void RemoveFinished()
		{
		}
	}
}
