using System.Diagnostics.CodeAnalysis;

namespace BilibiliLiveRecordDownLoader.Application.Storage;

[SuppressMessage("Design", "CA1001", Justification = "CancellationTokenSource 由 MonitorAsync 释放，字段只用来取消")]
internal sealed partial class LowDiskSpaceMonitor(IDiskSpaceService diskSpace, IAppConfigManager configManager, INotificationService notifications, TimeProvider timeProvider, ILogger<LowDiskSpaceMonitor> logger)
{
	internal const long Threshold = 1L << 30;

	internal static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

	private readonly Lock _lock = new();
	private int _watchers;
	private CancellationTokenSource? _cancellation;
	private Task _monitoring = Task.CompletedTask;
	private bool _isAlerted;

	public IDisposable Watch()
	{
		lock (_lock)
		{
			if (_watchers++ is 0)
			{
				_cancellation = new CancellationTokenSource();

				// 全局循环不能带着第一个开始录制的房间的日志作用域
				using (ExecutionContext.SuppressFlow())
				{
					_monitoring = MonitorAsync(_monitoring, _cancellation);
				}
			}
		}

		return new Watcher(this);
	}

	private void Unwatch()
	{
		lock (_lock)
		{
			if (--_watchers is 0)
			{
				_cancellation?.Cancel();
				_cancellation = null;
			}
		}
	}

	private async Task MonitorAsync(Task previous, CancellationTokenSource cancellation)
	{
		// 上一轮停止后才开始新一轮，提醒状态只由一个循环读写；previous 已完成时也要让出，否则第一次检查会在 Watch 持锁时同步执行
		await previous.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
		using PeriodicTimer timer = new(CheckInterval, timeProvider);

		try
		{
			do
			{
				await CheckAsync(cancellation.Token);
			}
			while (await timer.WaitForNextTickAsync(cancellation.Token));
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
		{
		}
		finally
		{
			cancellation.Dispose();
		}
	}

	private async Task CheckAsync(CancellationToken cancellationToken)
	{
		try
		{
			string directory = (await configManager.GetAsync(cancellationToken)).RecordDirectory;

			// 网络位置可能暂时不可达
			if (diskSpace.GetDiskSpace(directory) is not { AvailableBytes: var available })
			{
				return;
			}

			if (available >= Threshold)
			{
				_isAlerted = false;
			}
			else if (!_isAlerted)
			{
				_isAlerted = true;
				LogLowDiskSpace(logger, directory, available);
				notifications.Show(AppLanguage.Current.LowDiskSpace, AppLanguage.Current.LowDiskSpaceMessage.ToString(directory), AppNotificationType.Warning);
			}
		}
		catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
		{
			LogCheckFailed(logger, exception);
		}
	}

	private sealed class Watcher(LowDiskSpaceMonitor monitor) : IDisposable
	{
		private int _disposed;

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _disposed, 1) is 0)
			{
				monitor.Unwatch();
			}
		}
	}

	[LoggerMessage(Level = LogLevel.Warning, Message = "Only {AvailableBytes} bytes are free for recordings in {Directory}")]
	private static partial void LogLowDiskSpace(ILogger logger, string directory, long availableBytes);

	[LoggerMessage(Level = LogLevel.Debug, Message = "Failed to check the free disk space")]
	private static partial void LogCheckFailed(ILogger logger, Exception exception);
}
