using System.Collections.Concurrent;

namespace BilibiliLiveRecordDownLoader.Application.Tasks;

internal sealed partial class BackgroundTaskAppService(TimeProvider timeProvider, ILogger<BackgroundTaskAppService> logger) : IBackgroundTaskAppService
{
	// 长期无人值守时每个分段都会留下一条转封装任务，界面每次同步都要遍历全部条目
	internal const int MaxFinishedEntries = 200;

	private readonly Lock _lock = new();
	private readonly List<TaskEntry> _entries = [];
	private readonly ConcurrentDictionary<string, SemaphoreSlim> _queues = new(StringComparer.OrdinalIgnoreCase);
	private long _version;
	private bool _isStopping;

	public long Version => Interlocked.Read(ref _version);

	public bool HasActiveTasks
	{
		get
		{
			lock (_lock)
			{
				return _entries.Exists(static entry => entry.Snapshot.IsActive);
			}
		}
	}

	public IReadOnlyList<BackgroundTaskSnapshot> GetTasks()
	{
		lock (_lock)
		{
			return [.. _entries.Select(static entry => entry.Snapshot)];
		}
	}

	// 同一队列中的任务依次执行，避免多个转封装同时读写同一块磁盘
	public Task<BackgroundTaskSnapshot> Enqueue(string title, string queueKey, Func<BackgroundTaskContext, CancellationToken, Task> work, string? sourcePath = null, string? targetPath = null)
	{
		BackgroundTaskSnapshot snapshot = new(Guid.CreateVersion7(), title, sourcePath, targetPath, BackgroundTaskStatus.Queued, null, null, null);
		TaskEntry entry;

		lock (_lock)
		{
			if (_isStopping)
			{
				snapshot = snapshot with { Status = BackgroundTaskStatus.Canceled };
			}

			entry = new TaskEntry(snapshot);
			_entries.Add(entry);
			TrimFinished();
		}

		Touch();

		if (snapshot.IsActive)
		{
			_ = RunAsync(entry, _queues.GetOrAdd(queueKey, static _ => new SemaphoreSlim(1, 1)), work);
		}
		else
		{
			entry.Complete(snapshot);
		}

		return entry.Completion;
	}

	public void Cancel(Guid id)
	{
		CancellationTokenSource? cancellation;

		lock (_lock)
		{
			cancellation = _entries.Find(entry => entry.Snapshot.Id == id)?.Cancellation;
		}

		cancellation?.Cancel();
	}

	public void CancelAll()
	{
		TaskEntry[] entries;

		lock (_lock)
		{
			entries = [.. _entries];
		}

		CancelEach(entries);
	}

	public void RemoveFinished()
	{
		lock (_lock)
		{
			_entries.RemoveAll(static entry => !entry.Snapshot.IsActive);
		}

		Touch();
	}

	public async Task StopAsync()
	{
		TaskEntry[] entries;

		lock (_lock)
		{
			_isStopping = true;
			entries = [.. _entries];
		}

		CancelEach(entries);
		await Task.WhenAll(entries.Select(static entry => entry.Completion));
	}

	// 取消回调会就地跑完 FFmpeg 结束进程和任务的收尾，所以在锁外逐个取消
	private static void CancelEach(TaskEntry[] entries)
	{
		foreach (TaskEntry entry in entries)
		{
			entry.Cancellation.Cancel();
		}
	}

	// 要在 Enqueue 中同步进入队列才能按加入顺序执行，所以不先让出线程，而是不回到调用方的同步上下文：退出时 UI 线程在等这些任务
	private async Task RunAsync(TaskEntry entry, SemaphoreSlim queue, Func<BackgroundTaskContext, CancellationToken, Task> work)
	{
		CancellationToken cancellationToken = entry.Cancellation.Token;

		try
		{
			await queue.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			entry.Complete(Update(entry, BackgroundTaskStatus.Canceled));
			return;
		}

		BackgroundTaskSnapshot result;

		try
		{
			Update(entry, BackgroundTaskStatus.Running);
			BackgroundTaskContext context = new((progress, speed) => Update(entry, BackgroundTaskStatus.Running, progress, speed), timeProvider);
			await Task.Run(() => work(context, cancellationToken), cancellationToken).ConfigureAwait(false);
			result = Update(entry, BackgroundTaskStatus.Completed, 1);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			result = Update(entry, BackgroundTaskStatus.Canceled);
		}
		catch (Exception exception)
		{
			result = Update(entry, BackgroundTaskStatus.Failed, message: exception.Message);
			LogTaskFailed(logger, exception, result.Title);
		}
		finally
		{
			queue.Release();
		}

		entry.Complete(result);
	}

	// 速度只在运行中有意义，不保留旧值
	private BackgroundTaskSnapshot Update(TaskEntry entry, BackgroundTaskStatus status, double? progress = null, double? speed = null, string? message = null)
	{
		BackgroundTaskSnapshot snapshot;

		lock (_lock)
		{
			snapshot = entry.Snapshot with { Status = status, Progress = progress ?? entry.Snapshot.Progress, Speed = speed, Message = message ?? entry.Snapshot.Message };
			entry.Snapshot = snapshot;
		}

		Touch();
		return snapshot;
	}

	// 按加入顺序移除已结束的条目，失败的尽量留给用户查看
	private void TrimFinished()
	{
		List<TaskEntry> finished = _entries.FindAll(static entry => !entry.Snapshot.IsActive);

		if (finished.Count > MaxFinishedEntries)
		{
			HashSet<TaskEntry> removed = [.. finished.OrderBy(static entry => entry.Snapshot.Status is BackgroundTaskStatus.Failed).Take(finished.Count - MaxFinishedEntries)];
			_entries.RemoveAll(removed.Contains);
		}
	}

	private void Touch()
	{
		Interlocked.Increment(ref _version);
	}

	[LoggerMessage(Level = LogLevel.Error, Message = "Task {Title} failed")]
	private static partial void LogTaskFailed(ILogger logger, Exception exception, string title);

	private sealed class TaskEntry(BackgroundTaskSnapshot snapshot)
	{
		// 取消回调会同步跑到这里，等结果的调用方不能接着在取消它的线程上执行，那可能是 UI 线程
		private readonly TaskCompletionSource<BackgroundTaskSnapshot> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

		// 只在 _lock 内读写
		public BackgroundTaskSnapshot Snapshot { get; set; } = snapshot;

		// 不带定时器也不链接别的令牌，用完不必释放，取消也就不用和移除条目互斥
		public CancellationTokenSource Cancellation { get; } = new();

		public Task<BackgroundTaskSnapshot> Completion => _completion.Task;

		public void Complete(BackgroundTaskSnapshot result)
		{
			_completion.SetResult(result);
		}
	}
}
