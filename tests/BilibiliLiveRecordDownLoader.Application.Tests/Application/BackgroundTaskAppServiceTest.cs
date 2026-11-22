using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

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
			context.Report(1.5);
			reported.SetResult();
			await release.Task.WaitAsync(cancellationToken);
		}).Id;

		await reported.Task.WaitAsync(Timeout);
		BackgroundTaskSnapshot running = Snapshot(service, id);

		await Assert.That(running.Progress).IsEqualTo(1);

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
	public async Task StopWaitsForTasksEnqueuedConcurrentlyAsync()
	{
		for (int round = 0; round < 200; round++)
		{
			BackgroundTaskAppService service = CreateService();
			using Barrier barrier = new(2);

			Task enqueue = Task.Run(() =>
			{
				barrier.SignalAndWait();
				service.Enqueue("task", "q", static (_, cancellationToken) => Task.Delay(System.Threading.Timeout.Infinite, cancellationToken));
			});
			Task stop = Task.Run(async () =>
			{
				barrier.SignalAndWait();
				await service.StopAsync();
			});

			await Task.WhenAll(enqueue, stop).WaitAsync(Timeout);

			await Assert.That(service.HasActiveTasks).IsFalse();
		}
	}

	[Test]
	public async Task FinishedTasksAreCappedAndFailuresAreKeptLongestAsync()
	{
		BackgroundTaskAppService service = CreateService();

		BackgroundTaskSnapshot failed = await service.Enqueue("failing", "q", static (_, _) => throw new InvalidOperationException("boom")).Completion.WaitAsync(Timeout);
		List<Guid> completed = [];

		for (int i = 0; i < BackgroundTaskAppService.MaxFinishedEntries + 5; i++)
		{
			completed.Add((await service.Enqueue("done", "q", static (_, _) => Task.CompletedTask).Completion.WaitAsync(Timeout)).Id);
		}

		List<Guid> remaining = [.. service.GetTasks().Select(static task => task.Id)];

		// 加入最后一个任务时它还在排队，结束后要等下次加入才裁剪
		await Assert.That(remaining.Count).IsEqualTo(BackgroundTaskAppService.MaxFinishedEntries + 1);
		await Assert.That(remaining[0]).IsEqualTo(failed.Id);
		await Assert.That(remaining.Skip(1).SequenceEqual(completed.Skip(5))).IsTrue();
	}

	// 线程池上没有同步上下文，任务等的东西如果在取消回调里同步完成，任务的收尾也在取消回调里同步跑完
	[Test]
	public async Task CancelingAtTheCapFromAThreadPoolThreadCancelsEveryTaskAsync()
	{
		BackgroundTaskAppService service = CreateService();

		for (int i = 0; i < BackgroundTaskAppService.MaxFinishedEntries; i++)
		{
			await service.Enqueue("done", "a", static (_, _) => Task.CompletedTask).Completion.WaitAsync(Timeout);
		}

		TaskCompletionSource[] started = [new(TaskCreationOptions.RunContinuationsAsynchronously), new(TaskCreationOptions.RunContinuationsAsynchronously)];
		Guid[] running = [.. started.Select((signal, i) => service.Enqueue("running", $"b{i}", (_, cancellationToken) =>
		{
			TaskCompletionSource canceled = new();
			cancellationToken.Register(() => canceled.TrySetCanceled(cancellationToken));
			signal.SetResult();
			return canceled.Task;
		}).Id)];

		await Task.WhenAll(started.Select(static signal => signal.Task)).WaitAsync(Timeout);
		await Task.Run(service.StopAsync).WaitAsync(Timeout);

		await Assert.That(running.Select(id => Status(service, id))).IsEquivalentTo([BackgroundTaskStatus.Canceled, BackgroundTaskStatus.Canceled]);
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
	}

	// 工具页在 UI 线程上排队；退出时 UI 线程在等这些任务，续体投回去就再也不会执行
	[Test]
	public async Task TasksQueuedFromTheUIThreadDoNotContinueOnItAsync()
	{
		BackgroundTaskAppService service = CreateService();
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		StoppedSynchronizationContext ui = new();

		// 第二个任务要等第一个让出队列
		(Task<BackgroundTaskSnapshot> first, Task<BackgroundTaskSnapshot> second) = ui.Run(() => (service.Enqueue("first", "q", (_, cancellationToken) => release.Task.WaitAsync(cancellationToken)).Completion, service.Enqueue("second", "q", static (_, _) => Task.CompletedTask).Completion));
		release.SetResult();
		BackgroundTaskSnapshot[] finished = await Task.WhenAll(first, second).WaitAsync(Timeout);

		using (Assert.Multiple())
		{
			await Assert.That(finished.Select(static snapshot => snapshot.Status)).IsEquivalentTo([BackgroundTaskStatus.Completed, BackgroundTaskStatus.Completed]);
			await Assert.That(ui.Posted).IsEqualTo(0);
		}
	}

	[Test]
	public async Task FFmpegProgressUsesReportedDurationAndWeightAsync()
	{
		List<double?> values = [];
		BackgroundTaskContext context = new((progress, _) => values.Add(progress), TimeProvider.System);
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
		BackgroundTaskContext context = new((_, speed) => speeds.Add(speed), time);
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

	private static Task WaitForStatusAsync(BackgroundTaskAppService service, Guid id, BackgroundTaskStatus expected)
	{
		return TestWait.UntilAsync(() => Status(service, id) == expected, $"the task is {expected}", CancellationToken.None, Timeout);
	}
}
