using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class BackgroundTaskAppServiceTest
{
	private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

	[Test]
	public async Task TasksInSameQueueRunOneAtATimeInOrderAsync()
	{
		BackgroundTaskAppService service = CreateService();
		TaskCompletionSource releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
		List<string> started = [];

		Guid first = Start(service, "first", "C:", async (_, cancellationToken) =>
		{
			lock (started)
			{
				started.Add("first");
			}

			await releaseFirst.Task.WaitAsync(cancellationToken);
		});
		Guid second = Start(service, "second", "c:", (_, _) =>
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

		await Assert.That(started).IsEquivalentTo(["first", "second"], CollectionOrdering.Matching);
		await Assert.That(service.HasActiveTasks).IsFalse();
	}

	[Test]
	public async Task TasksInDifferentQueuesRunConcurrentlyAsync()
	{
		BackgroundTaskAppService service = CreateService();
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

		Guid first = Start(service, "first", "C:", (_, cancellationToken) => release.Task.WaitAsync(cancellationToken));
		Guid second = Start(service, "second", "D:", (_, cancellationToken) => release.Task.WaitAsync(cancellationToken));

		await WaitForStatusAsync(service, first, BackgroundTaskStatus.Running);
		await WaitForStatusAsync(service, second, BackgroundTaskStatus.Running);

		release.SetResult();
		await WaitForStatusAsync(service, second, BackgroundTaskStatus.Completed);
	}

	[Test]
	public async Task CancelStopsRunningAndQueuedTasksAsync()
	{
		BackgroundTaskAppService service = CreateService();

		Guid running = Start(service, "running", "q", static (_, cancellationToken) => Task.Delay(Timeout.Infinite, cancellationToken));
		Guid queued = Start(service, "queued", "q", static (_, _) => Task.CompletedTask);

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
		Guid id = Start(service, "late", "q", (_, _) =>
		{
			ran = true;
			return Task.CompletedTask;
		});

		await Assert.That(Status(service, id)).IsEqualTo(BackgroundTaskStatus.Canceled);
		await Assert.That(ran).IsFalse();
		await Assert.That(service.HasActiveTasks).IsFalse();
	}

	// 失败的任务之后，同一队列里的任务照常执行
	[Test]
	public async Task CompletionCarriesTheFinalSnapshotAsync()
	{
		BackgroundTaskAppService service = CreateService();

		BackgroundTaskSnapshot failed = await service.Enqueue("failing", "q", static (_, _) => throw new InvalidOperationException("boom")).WaitAsync(WaitTimeout);
		BackgroundTaskSnapshot completed = await service.Enqueue("done", "q", static (_, _) => Task.CompletedTask, "in.flv", "out.mp4").WaitAsync(WaitTimeout);
		await service.StopAsync();
		BackgroundTaskSnapshot canceled = await service.Enqueue("late", "q", static (_, _) => Task.CompletedTask).WaitAsync(WaitTimeout);

		using (Assert.Multiple())
		{
			await Assert.That((failed.Status, failed.Message)).IsEqualTo((BackgroundTaskStatus.Failed, "boom"));
			await Assert.That(completed).IsEqualTo(new BackgroundTaskSnapshot(completed.Id, "done", "in.flv", "out.mp4", BackgroundTaskStatus.Completed, 1, null, null));
			await Assert.That(canceled.Status).IsEqualTo(BackgroundTaskStatus.Canceled);
		}
	}

	[Test]
	public async Task ProgressIsClampedAndCompletionReportsFullProgressAsync()
	{
		BackgroundTaskAppService service = CreateService();
		TaskCompletionSource reported = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

		Guid id = Start(service, "task", "q", async (context, cancellationToken) =>
		{
			context.Report(1.5);
			reported.SetResult();
			await release.Task.WaitAsync(cancellationToken);
		});

		await reported.Task.WaitAsync(WaitTimeout);
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

		Guid done = Start(service, "done", "a", static (_, _) => Task.CompletedTask);
		Guid active = Start(service, "active", "b", (_, cancellationToken) => release.Task.WaitAsync(cancellationToken));

		await WaitForStatusAsync(service, done, BackgroundTaskStatus.Completed);
		await WaitForStatusAsync(service, active, BackgroundTaskStatus.Running);
		long version = service.Version;

		service.RemoveFinished();

		await Assert.That(service.GetTasks().Select(static task => task.Id)).IsEquivalentTo([active], CollectionOrdering.Matching);
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
				_ = service.Enqueue("task", "q", static (_, cancellationToken) => Task.Delay(Timeout.Infinite, cancellationToken));
			});
			Task stop = Task.Run(async () =>
			{
				barrier.SignalAndWait();
				await service.StopAsync();
			});

			await Task.WhenAll(enqueue, stop).WaitAsync(WaitTimeout);

			await Assert.That(service.HasActiveTasks).IsFalse();
		}
	}

	[Test]
	public async Task FinishedTasksAreCappedAndFailuresAreKeptLongestAsync()
	{
		BackgroundTaskAppService service = CreateService();

		BackgroundTaskSnapshot failed = await service.Enqueue("failing", "q", static (_, _) => throw new InvalidOperationException("boom")).WaitAsync(WaitTimeout);
		List<Guid> completed = [];

		for (int i = 0; i < BackgroundTaskAppService.MaxFinishedEntries + 5; i++)
		{
			completed.Add((await service.Enqueue("done", "q", static (_, _) => Task.CompletedTask).WaitAsync(WaitTimeout)).Id);
		}

		await Assert.That(service.GetTasks().Select(static task => task.Id)).IsEquivalentTo([failed.Id, .. completed.Skip(5)], CollectionOrdering.Matching).Because("加入最后一个任务时它还在排队，结束后要等下次加入才裁剪");
	}

	// 线程池上没有同步上下文，任务等的东西如果在取消回调里同步完成，任务的收尾也在取消回调里同步跑完
	[Test]
	public async Task CancelingAtTheCapFromAThreadPoolThreadCancelsEveryTaskAsync()
	{
		BackgroundTaskAppService service = CreateService();

		for (int i = 0; i < BackgroundTaskAppService.MaxFinishedEntries; i++)
		{
			await service.Enqueue("done", "a", static (_, _) => Task.CompletedTask).WaitAsync(WaitTimeout);
		}

		TaskCompletionSource[] started = [new(TaskCreationOptions.RunContinuationsAsynchronously), new(TaskCreationOptions.RunContinuationsAsynchronously)];
		Guid[] running = [.. started.Select((signal, i) => Start(service, "running", $"b{i}", (_, cancellationToken) =>
		{
			TaskCompletionSource canceled = new();
			cancellationToken.Register(() => canceled.TrySetCanceled(cancellationToken));
			signal.SetResult();
			return canceled.Task;
		}))];

		await Task.WhenAll(started.Select(static signal => signal.Task)).WaitAsync(WaitTimeout);
		await Task.Run(service.StopAsync).WaitAsync(WaitTimeout);

		await Assert.That(running.Select(id => Status(service, id))).IsEquivalentTo([BackgroundTaskStatus.Canceled, BackgroundTaskStatus.Canceled]);
	}

	// 工具页在 UI 线程上排队；退出时 UI 线程在等这些任务，续体投回去就再也不会执行
	[Test]
	public async Task TasksQueuedFromTheUIThreadDoNotContinueOnItAsync()
	{
		BackgroundTaskAppService service = CreateService();
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		StoppedSynchronizationContext ui = new();

		// 第二个任务要等第一个让出队列
		(Task<BackgroundTaskSnapshot> first, Task<BackgroundTaskSnapshot> second) = ui.Run(() => (service.Enqueue("first", "q", (_, cancellationToken) => release.Task.WaitAsync(cancellationToken)), service.Enqueue("second", "q", static (_, _) => Task.CompletedTask)));
		release.SetResult();
		BackgroundTaskSnapshot[] finished = await Task.WhenAll(first, second).WaitAsync(WaitTimeout);

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

		await Assert.That(values).IsEquivalentTo((double?[])[null, 0.75, 1], CollectionOrdering.Matching);
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

		await Assert.That(speeds).IsEquivalentTo((double?[])[null, 25], CollectionOrdering.Matching);
	}

	private static BackgroundTaskAppService CreateService()
	{
		return new BackgroundTaskAppService(TimeProvider.System, NullLogger<BackgroundTaskAppService>.Instance);
	}

	// Enqueue 只交出完成结果；刚加入的任务排在列表最后
	private static Guid Start(BackgroundTaskAppService service, string title, string queueKey, Func<BackgroundTaskContext, CancellationToken, Task> work)
	{
		_ = service.Enqueue(title, queueKey, work);
		return service.GetTasks()[^1].Id;
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
		return TestWait.UntilAsync(() => Status(service, id) == expected, $"the task is {expected}", CancellationToken.None, WaitTimeout);
	}
}
