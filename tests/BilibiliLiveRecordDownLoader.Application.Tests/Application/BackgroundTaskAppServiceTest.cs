namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class BackgroundTaskAppServiceTest
{
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

	[Test]
	public async Task TasksInSameQueueRunOneAtATimeInOrderAsync()
	{
		BackgroundTaskAppService service = CreateService();
		TaskCompletionSource releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
		List<string> started = [];

		Guid first = service.Enqueue("first", "C:", async (_, cancellationToken) =>
		{
			lock (started)
			{
				started.Add("first");
			}

			await releaseFirst.Task.WaitAsync(cancellationToken);
		}).Id;
		Guid second = service.Enqueue("second", "c:", (_, _) =>
		{
			lock (started)
			{
				started.Add("second");
			}

			return Task.CompletedTask;
		}).Id;

		await WaitForStatusAsync(service, first, BackgroundTaskStatus.Running);
		await Assert.That(Status(service, second)).IsEqualTo(BackgroundTaskStatus.Queued);

		releaseFirst.SetResult();
		await WaitForStatusAsync(service, second, BackgroundTaskStatus.Completed);

		await Assert.That(started.SequenceEqual(["first", "second"])).IsTrue();
		await Assert.That(service.HasActiveTasks).IsFalse();
	}

	[Test]
	public async Task TasksInDifferentQueuesRunConcurrentlyAsync()
	{
		BackgroundTaskAppService service = CreateService();
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

		Guid first = service.Enqueue("first", "C:", (_, cancellationToken) => release.Task.WaitAsync(cancellationToken)).Id;
		Guid second = service.Enqueue("second", "D:", (_, cancellationToken) => release.Task.WaitAsync(cancellationToken)).Id;

		await WaitForStatusAsync(service, first, BackgroundTaskStatus.Running);
		await WaitForStatusAsync(service, second, BackgroundTaskStatus.Running);

		release.SetResult();
		await WaitForStatusAsync(service, second, BackgroundTaskStatus.Completed);
	}

	[Test]
	public async Task CancelStopsRunningAndQueuedTasksAsync()
	{
		BackgroundTaskAppService service = CreateService();

		Guid running = service.Enqueue("running", "q", static (_, cancellationToken) => Task.Delay(System.Threading.Timeout.Infinite, cancellationToken)).Id;
		Guid queued = service.Enqueue("queued", "q", static (_, _) => Task.CompletedTask).Id;

		await WaitForStatusAsync(service, running, BackgroundTaskStatus.Running);
		service.Cancel(queued);
		await WaitForStatusAsync(service, queued, BackgroundTaskStatus.Canceled);
		await Assert.That(Status(service, running)).IsEqualTo(BackgroundTaskStatus.Running);

		await service.StopAsync();

		await Assert.That(Status(service, running)).IsEqualTo(BackgroundTaskStatus.Canceled);
	}

	[Test]
	public async Task TasksQueuedAfterStopAreCanceledWithoutRunningAsync()
	{
		BackgroundTaskAppService service = CreateService();
		bool ran = false;

		await service.StopAsync();
		Guid id = service.Enqueue("late", "q", (_, _) =>
		{
			ran = true;
			return Task.CompletedTask;
		}).Id;

		await Assert.That(Status(service, id)).IsEqualTo(BackgroundTaskStatus.Canceled);
		await Assert.That(ran).IsFalse();
		await Assert.That(service.HasActiveTasks).IsFalse();
	}

	[Test]
	public async Task FailureIsReportedAndDoesNotBlockQueueAsync()
	{
		BackgroundTaskAppService service = CreateService();

		Guid failing = service.Enqueue("failing", "q", static (_, _) => throw new InvalidOperationException("boom")).Id;
		Guid next = service.Enqueue("next", "q", static (_, _) => Task.CompletedTask).Id;

		await WaitForStatusAsync(service, next, BackgroundTaskStatus.Completed);
		BackgroundTaskSnapshot snapshot = Snapshot(service, failing);

		await Assert.That(snapshot.Status).IsEqualTo(BackgroundTaskStatus.Failed);
		await Assert.That(snapshot.Message).IsEqualTo("boom");
	}

	[Test]
	public async Task CompletionCarriesTheFinalSnapshotAsync()
	{
		BackgroundTaskAppService service = CreateService();

		BackgroundTaskSnapshot failed = await service.Enqueue("failing", "q", static (_, _) => throw new InvalidOperationException("boom")).Completion.WaitAsync(Timeout);
		BackgroundTaskSnapshot completed = await service.Enqueue("done", "q", static (_, _) => Task.CompletedTask).Completion.WaitAsync(Timeout);
		await service.StopAsync();
		BackgroundTaskSnapshot canceled = await service.Enqueue("late", "q", static (_, _) => Task.CompletedTask).Completion.WaitAsync(Timeout);

		using (Assert.Multiple())
		{
			await Assert.That((failed.Status, failed.Message)).IsEqualTo((BackgroundTaskStatus.Failed, "boom"));
			await Assert.That(completed.Status).IsEqualTo(BackgroundTaskStatus.Completed);
			await Assert.That(canceled.Status).IsEqualTo(BackgroundTaskStatus.Canceled);
		}
	}

	[Test]
	public async Task ProgressIsClampedAndCompletionReportsFullProgressAsync()
	{
		BackgroundTaskAppService service = CreateService();
		TaskCompletionSource reported = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

		Guid id = service.Enqueue("task", "q", async (context, cancellationToken) =>
		{
			context.Report(1.5, "working");
			reported.SetResult();
			await release.Task.WaitAsync(cancellationToken);
		}).Id;

		await reported.Task.WaitAsync(Timeout);
		BackgroundTaskSnapshot running = Snapshot(service, id);

		await Assert.That(running.Progress).IsEqualTo(1);
		await Assert.That(running.Message).IsEqualTo("working");

		release.SetResult();
		await WaitForStatusAsync(service, id, BackgroundTaskStatus.Completed);
		await Assert.That(Snapshot(service, id).Progress).IsEqualTo(1);
	}

	[Test]
	public async Task RemoveFinishedKeepsActiveTasksAsync()
	{
		BackgroundTaskAppService service = CreateService();
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

		Guid done = service.Enqueue("done", "a", static (_, _) => Task.CompletedTask).Id;
		Guid active = service.Enqueue("active", "b", (_, cancellationToken) => release.Task.WaitAsync(cancellationToken)).Id;

		await WaitForStatusAsync(service, done, BackgroundTaskStatus.Completed);
		await WaitForStatusAsync(service, active, BackgroundTaskStatus.Running);
		long version = service.Version;

		service.RemoveFinished();

		await Assert.That(service.GetTasks().Select(static task => task.Id).SequenceEqual([active])).IsTrue();
		await Assert.That(service.Version).IsGreaterThan(version);

		release.SetResult();
		await WaitForStatusAsync(service, active, BackgroundTaskStatus.Completed);
	}

	[Test]
	public async Task SnapshotCarriesSourceAndTargetPathsAsync()
	{
		BackgroundTaskAppService service = CreateService();

		Guid id = service.Enqueue("task", "q", static (_, _) => Task.CompletedTask, "in.flv", "out.mp4").Id;
		await WaitForStatusAsync(service, id, BackgroundTaskStatus.Completed);

		BackgroundTaskSnapshot snapshot = Snapshot(service, id);
		await Assert.That(snapshot.SourcePath).IsEqualTo("in.flv");
		await Assert.That(snapshot.TargetPath).IsEqualTo("out.mp4");
		await Assert.That(snapshot.Speed).IsNull();
	}

	[Test]
	public async Task FFmpegProgressUsesReportedDurationAndWeightAsync()
	{
		List<double?> values = [];
		BackgroundTaskContext context = new((progress, _, _) => values.Add(progress), TimeProvider.System);
		IProgress<FFmpegProgress> progress = context.CreateFFmpegProgress(start: 0.5, weight: 0.5);

		progress.Report(new FFmpegProgress(TimeSpan.FromSeconds(100), null, "start"));
		progress.Report(new FFmpegProgress(null, TimeSpan.FromSeconds(50), "half"));
		progress.Report(new FFmpegProgress(null, TimeSpan.FromSeconds(200), "overflow"));

		await Assert.That(values.SequenceEqual([null, 0.75, 1.0])).IsTrue();
	}

	[Test]
	public async Task FFmpegProgressReportsMediaTimePerElapsedTimeAsSpeedAsync()
	{
		FakeTimeProvider time = new();
		List<double?> speeds = [];
		BackgroundTaskContext context = new((_, speed, _) => speeds.Add(speed), time);
		IProgress<FFmpegProgress> progress = context.CreateFFmpegProgress();

		progress.Report(new FFmpegProgress(TimeSpan.FromSeconds(100), null, "start"));
		time.Advance(TimeSpan.FromSeconds(2));
		progress.Report(new FFmpegProgress(null, TimeSpan.FromSeconds(50), "half"));

		await Assert.That(speeds.SequenceEqual([null, 25.0])).IsTrue();
	}

	private static BackgroundTaskAppService CreateService()
	{
		return new BackgroundTaskAppService(TimeProvider.System, NullLogger<BackgroundTaskAppService>.Instance);
	}

	private static BackgroundTaskSnapshot Snapshot(BackgroundTaskAppService service, Guid id)
	{
		return service.GetTasks().Single(task => task.Id == id);
	}

	private static BackgroundTaskStatus Status(BackgroundTaskAppService service, Guid id)
	{
		return Snapshot(service, id).Status;
	}

	private static async Task WaitForStatusAsync(BackgroundTaskAppService service, Guid id, BackgroundTaskStatus expected)
	{
		using CancellationTokenSource timeout = new(Timeout);

		while (Status(service, id) != expected)
		{
			await Task.Delay(10, timeout.Token);
		}
	}
}
