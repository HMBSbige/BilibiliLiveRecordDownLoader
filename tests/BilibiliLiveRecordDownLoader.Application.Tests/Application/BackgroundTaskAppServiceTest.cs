using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class BackgroundTaskAppServiceTest
{
	private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

	[Test]
	public async Task TasksInSameQueueRunOneAtATimeInOrderAsync()
	{
		BackgroundTaskAppService service = CreateService();
		TaskCompletionSource firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
		List<string> started = [];

		_ = service.Enqueue("first", "C:", async (_, cancellationToken) =>
		{
			lock (started)
			{
				started.Add("first");
			}

			firstStarted.SetResult();
			await releaseFirst.Task.WaitAsync(cancellationToken);
		});
		(Guid secondId, Task<BackgroundTaskSnapshot> second) = Start(service, "second", "c:", (_, _) =>
		{
			lock (started)
			{
				started.Add("second");
			}

			return Task.CompletedTask;
		});

		await firstStarted.Task.WaitAsync(WaitTimeout);
		await Assert.That(Status(service, secondId)).IsEqualTo(BackgroundTaskStatus.Queued);

		releaseFirst.SetResult();
		await second.WaitAsync(WaitTimeout);

		await Assert.That(started).IsEquivalentTo(["first", "second"], CollectionOrdering.Matching);
		await Assert.That(service.HasActiveTasks).IsFalse();
	}

	// 两个任务互相等对方开始，只能同时运行才都能结束
	[Test]
	public async Task TasksInDifferentQueuesRunConcurrentlyAsync()
	{
		BackgroundTaskAppService service = CreateService();
		TaskCompletionSource onC = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource onD = new(TaskCreationOptions.RunContinuationsAsynchronously);

		BackgroundTaskSnapshot[] finished = await Task.WhenAll(service.Enqueue("c", "C:", Meet(onC, onD)), service.Enqueue("d", "D:", Meet(onD, onC))).WaitAsync(WaitTimeout);

		await Assert.That(finished.Select(static snapshot => snapshot.Status)).All(static status => status is BackgroundTaskStatus.Completed);

		static Func<BackgroundTaskContext, CancellationToken, Task> Meet(TaskCompletionSource arrived, TaskCompletionSource other)
		{
			return (_, cancellationToken) =>
			{
				arrived.SetResult();
				return other.Task.WaitAsync(cancellationToken);
			};
		}
	}

	[Test]
	public async Task CancelStopsRunningAndQueuedTasksAsync()
	{
		BackgroundTaskAppService service = CreateService();
		TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);

		(Guid runningId, Task<BackgroundTaskSnapshot> running) = Start(service, "running", "q", async (_, cancellationToken) =>
		{
			started.SetResult();
			await Task.Delay(Timeout.Infinite, cancellationToken);
		});
		(Guid queuedId, Task<BackgroundTaskSnapshot> queued) = Start(service, "queued", "q", static (_, _) => Task.CompletedTask);

		await started.Task.WaitAsync(WaitTimeout);
		service.Cancel(queuedId);
		BackgroundTaskSnapshot canceled = await queued.WaitAsync(WaitTimeout);
		BackgroundTaskStatus stillRunning = Status(service, runningId);
		await service.StopAsync();

		await Assert.That((canceled.Status, stillRunning, (await running).Status)).IsEqualTo((BackgroundTaskStatus.Canceled, BackgroundTaskStatus.Running, BackgroundTaskStatus.Canceled));
	}

	[Test]
	public async Task TasksQueuedAfterStopAreCanceledWithoutRunningAsync()
	{
		BackgroundTaskAppService service = CreateService();
		bool ran = false;

		await service.StopAsync();
		BackgroundTaskSnapshot late = await service.Enqueue("late", "q", (_, _) =>
		{
			ran = true;
			return Task.CompletedTask;
		}).WaitAsync(WaitTimeout);

		await Assert.That((late.Status, ran, service.HasActiveTasks)).IsEqualTo((BackgroundTaskStatus.Canceled, false, false));
	}

	// 失败的任务之后，同一队列里的任务照常执行
	[Test]
	public async Task CompletionCarriesTheFinalSnapshotAsync()
	{
		BackgroundTaskAppService service = CreateService();

		BackgroundTaskSnapshot failed = await service.Enqueue("failing", "q", static (_, _) => throw new InvalidOperationException("boom")).WaitAsync(WaitTimeout);
		BackgroundTaskSnapshot completed = await service.Enqueue("done", "q", static (_, _) => Task.CompletedTask, "in.flv", "out.mp4").WaitAsync(WaitTimeout);

		using (Assert.Multiple())
		{
			await Assert.That((failed.Status, failed.Message)).IsEqualTo((BackgroundTaskStatus.Failed, "boom"));
			await Assert.That(completed).IsEqualTo(new BackgroundTaskSnapshot(completed.Id, "done", "in.flv", "out.mp4", BackgroundTaskStatus.Completed, 1, null, null));
		}
	}

	[Test]
	public async Task ProgressIsClampedAndCompletionReportsFullProgressAsync()
	{
		BackgroundTaskAppService service = CreateService();
		TaskCompletionSource reported = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

		(Guid id, Task<BackgroundTaskSnapshot> completion) = Start(service, "task", "q", async (context, cancellationToken) =>
		{
			context.CreateProgress().Report(1.5);
			reported.SetResult();
			await release.Task.WaitAsync(cancellationToken);
		});

		await reported.Task.WaitAsync(WaitTimeout);
		double? running = Snapshot(service, id).Progress;
		release.SetResult();

		await Assert.That((running, (await completion.WaitAsync(WaitTimeout)).Progress)).IsEqualTo((1d, 1d));
	}

	[Test]
	public async Task RemoveFinishedKeepsActiveTasksAsync()
	{
		BackgroundTaskAppService service = CreateService();
		TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

		await service.Enqueue("done", "a", static (_, _) => Task.CompletedTask).WaitAsync(WaitTimeout);
		(Guid active, Task<BackgroundTaskSnapshot> completion) = Start(service, "active", "b", async (_, cancellationToken) =>
		{
			started.SetResult();
			await release.Task.WaitAsync(cancellationToken);
		});

		await started.Task.WaitAsync(WaitTimeout);
		long version = service.Version;

		service.RemoveFinished();

		await Assert.That(service.GetTasks().Select(static task => task.Id)).IsEquivalentTo([active], CollectionOrdering.Matching);
		await Assert.That(service.Version).IsGreaterThan(version);

		release.SetResult();
		await completion.WaitAsync(WaitTimeout);
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

	// 线程池上没有同步上下文，任务等的东西在取消回调里同步完成时，任务的收尾和等结果的调用方都可能在取消的调用里接着执行，调用方这时排入新任务
	[Test]
	public async Task CallersQueueingFollowUpsWhileTasksAreCanceledDoNotBreakCancelAllAsync()
	{
		BackgroundTaskAppService service = CreateService();
		TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);

		Task<BackgroundTaskSnapshot> running = service.Enqueue("running", "q", (_, cancellationToken) =>
		{
			TaskCompletionSource canceled = new();
			cancellationToken.Register(() => canceled.TrySetCanceled(cancellationToken));
			started.SetResult();
			return canceled.Task;
		});
		Task<BackgroundTaskSnapshot> followUp = running.ContinueWith(_ => service.Enqueue("follow-up", "q", static (_, _) => Task.CompletedTask), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default).Unwrap();

		await started.Task.WaitAsync(WaitTimeout);
		await Task.Run(service.CancelAll).WaitAsync(WaitTimeout);

		await Assert.That((await followUp.WaitAsync(WaitTimeout)).Status).IsEqualTo(BackgroundTaskStatus.Completed);
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

	// 刚加入的任务排在列表最后
	private static (Guid Id, Task<BackgroundTaskSnapshot> Completion) Start(BackgroundTaskAppService service, string title, string queueKey, Func<BackgroundTaskContext, CancellationToken, Task> work)
	{
		Task<BackgroundTaskSnapshot> completion = service.Enqueue(title, queueKey, work);
		return (service.GetTasks()[^1].Id, completion);
	}

	private static BackgroundTaskSnapshot Snapshot(BackgroundTaskAppService service, Guid id)
	{
		return service.GetTasks().Single(task => task.Id == id);
	}

	private static BackgroundTaskStatus Status(BackgroundTaskAppService service, Guid id)
	{
		return Snapshot(service, id).Status;
	}
}
