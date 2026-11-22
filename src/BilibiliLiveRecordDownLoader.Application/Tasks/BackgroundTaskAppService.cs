using System.Collections.Concurrent;

namespace BilibiliLiveRecordDownLoader.Application.Tasks;

internal sealed partial class BackgroundTaskAppService(TimeProvider timeProvider, ILogger<BackgroundTaskAppService> logger) : IBackgroundTaskAppService
{
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
				return _entries.Any(static entry => entry.IsActive);
			}
		}
	}

	public IReadOnlyList<BackgroundTaskSnapshot> GetTasks()
	{
		lock (_lock)
		{
			return [.. _entries.Select(static entry => entry.ToSnapshot())];
		}
	}

	// 同一队列中的任务依次执行，避免多个转封装同时读写同一块磁盘；返回的 Completion 在任务结束时给出最终快照
	public QueuedTask Enqueue(string title, string queueKey, Func<BackgroundTaskContext, CancellationToken, Task> work, string? sourcePath = null, string? targetPath = null)
	{
		TaskEntry entry = new(Guid.CreateVersion7(), title, sourcePath, targetPath);
		bool isStopping;

		lock (_lock)
		{
			_entries.Add(entry);
			isStopping = _isStopping;

			// 退出过程中录制收尾产生的任务不再执行，源文件保持原样
			if (isStopping)
			{
				entry.Status = BackgroundTaskStatus.Canceled;
			}
		}

		Touch();

		Task<BackgroundTaskSnapshot> completion = isStopping ? Task.FromResult(Snapshot(entry)) : RunAsync(entry, _queues.GetOrAdd(queueKey, static _ => new SemaphoreSlim(1, 1)), work);
		entry.Completion = completion;

		return new QueuedTask(entry.Id, completion);
	}

	public void Cancel(Guid id)
	{
		lock (_lock)
		{
			_entries.Find(entry => entry.Id == id)?.Cancel();
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
			_entries.RemoveAll(static entry => !entry.IsActive);
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

	// 在 Enqueue 中同步进入队列，保证同一队列按加入顺序执行
	private async Task<BackgroundTaskSnapshot> RunAsync(TaskEntry entry, SemaphoreSlim queue, Func<BackgroundTaskContext, CancellationToken, Task> work)
	{
		CancellationToken cancellationToken = entry.CancellationToken;

		try
		{
			await queue.WaitAsync(cancellationToken);
		}
		catch (OperationCanceledException)
		{
			Update(entry, BackgroundTaskStatus.Canceled);
			return Snapshot(entry);
		}

		try
		{
			Update(entry, BackgroundTaskStatus.Running);
			BackgroundTaskContext context = new((progress, speed, message) => Update(entry, BackgroundTaskStatus.Running, progress, speed, message), timeProvider);
			await Task.Run(() => work(context, cancellationToken), cancellationToken);
			Update(entry, BackgroundTaskStatus.Completed, 1);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			Update(entry, BackgroundTaskStatus.Canceled);
		}
		catch (Exception exception)
		{
			LogTaskFailed(logger, exception, entry.Title);
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
			return entry.ToSnapshot();
		}
	}

	// 速度只在运行中有意义，每次都用最新的值覆盖
	private void Update(TaskEntry entry, BackgroundTaskStatus status, double? progress = null, double? speed = null, string? message = null)
	{
		lock (_lock)
		{
			entry.Status = status;
			entry.Progress = progress ?? entry.Progress;
			entry.Speed = speed;
			entry.Message = message ?? entry.Message;
		}

		Touch();
	}

	private void Touch()
	{
		Interlocked.Increment(ref _version);
	}

	[LoggerMessage(Level = LogLevel.Error, Message = "Task {Title} failed")]
	private static partial void LogTaskFailed(ILogger logger, Exception exception, string title);

	private sealed class TaskEntry(Guid id, string title, string? sourcePath, string? targetPath)
	{
		private readonly CancellationTokenSource _cancellation = new();

		public Guid Id { get; } = id;

		public string Title { get; } = title;

		public string? SourcePath { get; } = sourcePath;

		public string? TargetPath { get; } = targetPath;

		public BackgroundTaskStatus Status { get; set; }

		public double? Progress { get; set; }

		public double? Speed { get; set; }

		public string? Message { get; set; }

		public Task Completion { get; set; } = Task.CompletedTask;

		public CancellationToken CancellationToken => _cancellation.Token;

		public bool IsActive => Status is BackgroundTaskStatus.Queued or BackgroundTaskStatus.Running;

		public void Cancel()
		{
			if (IsActive)
			{
				_cancellation.Cancel();
			}
		}

		public BackgroundTaskSnapshot ToSnapshot()
		{
			return new BackgroundTaskSnapshot(Id, Title, SourcePath, TargetPath, Status, Progress, Speed, Message);
		}
	}
}
