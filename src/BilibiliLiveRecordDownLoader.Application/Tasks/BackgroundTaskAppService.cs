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
		Guid id = Guid.CreateVersion7();
		TaskEntry entry = new(new BackgroundTaskSnapshot(id, title, sourcePath, targetPath, BackgroundTaskStatus.Queued, null, null, null));
		SemaphoreSlim queue = _queues.GetOrAdd(queueKey, static _ => new SemaphoreSlim(1, 1));
		// Completion 要在加入列表的同时可见，否则 StopAsync 不会等这个任务
		Task<Task<BackgroundTaskSnapshot>> start = new(() => RunAsync(entry, queue, work));
		Task<BackgroundTaskSnapshot> completion = start.Unwrap();
		bool isStopping;

		lock (_lock)
		{
			isStopping = _isStopping;

			if (isStopping)
			{
				entry.Snapshot = entry.Snapshot with { Status = BackgroundTaskStatus.Canceled };
				completion = Task.FromResult(entry.Snapshot);
			}

			entry.Completion = completion;
			_entries.Add(entry);
			TrimFinished();
		}

		Touch();

		if (!isStopping)
		{
			start.RunSynchronously(TaskScheduler.Default);
		}

		return completion;
	}

	public void Cancel(Guid id)
	{
		lock (_lock)
		{
			_entries.Find(entry => entry.Snapshot.Id == id)?.Cancel();
		}
	}

	public void CancelAll()
	{
		lock (_lock)
		{
			_entries.ForEach(static entry => entry.Cancel());
		}
	}

	public void RemoveFinished()
	{
		lock (_lock)
		{
			RemoveOldestFinished(static _ => true, int.MaxValue);
		}

		Touch();
	}

	public async Task StopAsync()
	{
		Task[] pending;

		lock (_lock)
		{
			_isStopping = true;
			_entries.ForEach(static entry => entry.Cancel());
			pending = [.. _entries.Select(static entry => entry.Completion)];
		}

		await Task.WhenAll(pending);
	}

	// 要在 Enqueue 中同步进入队列才能按加入顺序执行，所以不先让出线程，而是不回到调用方的同步上下文：退出时 UI 线程在等这些任务
	private async Task<BackgroundTaskSnapshot> RunAsync(TaskEntry entry, SemaphoreSlim queue, Func<BackgroundTaskContext, CancellationToken, Task> work)
	{
		CancellationToken cancellationToken = entry.CancellationToken;

		try
		{
			await queue.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			Update(entry, BackgroundTaskStatus.Canceled);
			return Snapshot(entry);
		}

		try
		{
			Update(entry, BackgroundTaskStatus.Running);
			BackgroundTaskContext context = new((progress, speed) => Update(entry, BackgroundTaskStatus.Running, progress, speed), timeProvider);
			await Task.Run(() => work(context, cancellationToken), cancellationToken).ConfigureAwait(false);
			Update(entry, BackgroundTaskStatus.Completed, 1);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			Update(entry, BackgroundTaskStatus.Canceled);
		}
		catch (Exception exception)
		{
			LogTaskFailed(logger, exception, Snapshot(entry).Title);
			Update(entry, BackgroundTaskStatus.Failed, message: exception.Message);
		}
		finally
		{
			queue.Release();
		}

		return Snapshot(entry);
	}

	private BackgroundTaskSnapshot Snapshot(TaskEntry entry)
	{
		lock (_lock)
		{
			return entry.Snapshot;
		}
	}

	// 速度只在运行中有意义，不保留旧值
	private void Update(TaskEntry entry, BackgroundTaskStatus status, double? progress = null, double? speed = null, string? message = null)
	{
		lock (_lock)
		{
			BackgroundTaskSnapshot snapshot = entry.Snapshot;
			entry.Snapshot = snapshot with { Status = status, Progress = progress ?? snapshot.Progress, Speed = speed, Message = message ?? snapshot.Message };
		}

		Touch();
	}

	// 失败的条目尽量留给用户查看
	// 只在加入新条目时裁剪：取消回调可能在 CancelAll 和 StopAsync 遍历列表时同步跑到 Update
	private void TrimFinished()
	{
		int excess = _entries.Count(static entry => !entry.Snapshot.IsActive) - MaxFinishedEntries;

		if (excess > 0)
		{
			excess -= RemoveOldestFinished(static entry => entry.Snapshot.Status is not BackgroundTaskStatus.Failed, excess);
		}

		if (excess > 0)
		{
			RemoveOldestFinished(static entry => entry.Snapshot.Status is BackgroundTaskStatus.Failed, excess);
		}
	}

	private int RemoveOldestFinished(Func<TaskEntry, bool> predicate, int count)
	{
		int removed = 0;

		for (int i = 0; i < _entries.Count && removed < count;)
		{
			TaskEntry entry = _entries[i];

			if (!entry.Snapshot.IsActive && predicate(entry))
			{
				entry.Dispose();
				_entries.RemoveAt(i);
				removed++;
			}
			else
			{
				i++;
			}
		}

		return removed;
	}

	private void Touch()
	{
		Interlocked.Increment(ref _version);
	}

	[LoggerMessage(Level = LogLevel.Error, Message = "Task {Title} failed")]
	private static partial void LogTaskFailed(ILogger logger, Exception exception, string title);

	private sealed class TaskEntry(BackgroundTaskSnapshot snapshot) : IDisposable
	{
		private readonly CancellationTokenSource _cancellation = new();

		// 只在 _lock 内读写
		public BackgroundTaskSnapshot Snapshot { get; set; } = snapshot;

		public Task Completion { get; set; } = Task.CompletedTask;

		public CancellationToken CancellationToken => _cancellation.Token;

		public void Cancel()
		{
			if (Snapshot.IsActive)
			{
				_cancellation.Cancel();
			}
		}

		public void Dispose()
		{
			_cancellation.Dispose();
		}
	}
}
