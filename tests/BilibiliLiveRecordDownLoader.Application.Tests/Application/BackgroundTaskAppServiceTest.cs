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
		});
		Guid second = service.Enqueue("second", "c:", (_, _) =>
		{
			lock (started)
			{
				started.Add("second");
			}

			return Task.CompletedTask;
		});

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

		Guid first = service.Enqueue("first", "C:", (_, cancellationToken) => release.Task.WaitAsync(cancellationToken));
		Guid second = service.Enqueue("second", "D:", (_, cancellationToken) => release.Task.WaitAsync(cancellationToken));

		await WaitForStatusAsync(service, first, BackgroundTaskStatus.Running);
		await WaitForStatusAsync(service, second, BackgroundTaskStatus.Running);

		release.SetResult();
		await WaitForStatusAsync(service, second, BackgroundTaskStatus.Completed);
	}

	[Test]
	public async Task CancelStopsRunningAndQueuedTasksAsync()
	{
		BackgroundTaskAppService service = CreateService();

		Guid running = service.Enqueue("running", "q", static (_, cancellationToken) => Task.Delay(System.Threading.Timeout.Infinite, cancellationToken));
		Guid queued = service.Enqueue("queued", "q", static (_, _) => Task.CompletedTask);

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
		});

		await Assert.That(Status(service, id)).IsEqualTo(BackgroundTaskStatus.Canceled);
		await Assert.That(ran).IsFalse();
		await Assert.That(service.HasActiveTasks).IsFalse();
	}

	[Test]
	public async Task FailureIsReportedAndDoesNotBlockQueueAsync()
	{
		BackgroundTaskAppService service = CreateService();

		Guid failing = service.Enqueue("failing", "q", static (_, _) => throw new InvalidOperationException("boom"));
		Guid next = service.Enqueue("next", "q", static (_, _) => Task.CompletedTask);

		await WaitForStatusAsync(service, next, BackgroundTaskStatus.Completed);
		BackgroundTaskSnapshot snapshot = Snapshot(service, failing);

		await Assert.That(snapshot.Status).IsEqualTo(BackgroundTaskStatus.Failed);
		await Assert.That(snapshot.Message).IsEqualTo("boom");
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
		});

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

		Guid done = service.Enqueue("done", "a", static (_, _) => Task.CompletedTask);
		Guid active = service.Enqueue("active", "b", (_, cancellationToken) => release.Task.WaitAsync(cancellationToken));

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
	public async Task FFmpegProgressUsesReportedDurationAndWeightAsync()
	{
		List<double?> values = [];
		BackgroundTaskContext context = new((progress, _) => values.Add(progress));
		IProgress<FFmpegProgress> progress = context.CreateFFmpegProgress(start: 0.5, weight: 0.5);

		progress.Report(new FFmpegProgress(TimeSpan.FromSeconds(100), null, "start"));
		progress.Report(new FFmpegProgress(null, TimeSpan.FromSeconds(50), "half"));
		progress.Report(new FFmpegProgress(null, TimeSpan.FromSeconds(200), "overflow"));

		await Assert.That(values.SequenceEqual([null, 0.75, 1.0])).IsTrue();
	}

	private static BackgroundTaskAppService CreateService()
	{
		return new BackgroundTaskAppService(NullLogger<BackgroundTaskAppService>.Instance);
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
