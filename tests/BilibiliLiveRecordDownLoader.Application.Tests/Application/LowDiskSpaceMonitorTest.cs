using BilibiliLiveRecordDownLoader.Application.Contracts.Storage;
using BilibiliLiveRecordDownLoader.Application.Storage;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class LowDiskSpaceMonitorTest
{
	private const long Low = DiskSpaceInfo.LowThreshold - 1;
	private const long Enough = DiskSpaceInfo.LowThreshold;

	[Test]
	public async Task AlertsOnceWhileLowAndAgainOnlyAfterRecoveringAsync(CancellationToken cancellationToken)
	{
		(LowDiskSpaceMonitor monitor, FakeDiskSpace disk, RecordingNotifications notifications, FakeTimeProvider time) = Create();
		using IDisposable watch = monitor.Watch();
		await disk.WaitForCheckAsync(cancellationToken);

		foreach (long? available in new long?[] { Enough, Low, Low, Enough, Low, Low })
		{
			disk.Available = available;
			time.Advance(LowDiskSpaceMonitor.CheckInterval);
			await disk.WaitForCheckAsync(cancellationToken);
		}

		using (Assert.Multiple())
		{
			await Assert.That(notifications.Shown).Count().IsEqualTo(2);
			await Assert.That(notifications.Shown).All(static shown => shown.Type is AppNotificationType.Warning);
		}
	}

	// 停止监控期间空间可能恢复过又被占满
	[Test]
	public async Task AlertsAgainWhenMonitoringRestartsWhileLowAsync(CancellationToken cancellationToken)
	{
		(LowDiskSpaceMonitor monitor, FakeDiskSpace disk, RecordingNotifications notifications, _) = Create();
		disk.Available = Low;

		using (monitor.Watch())
		{
			await TestWait.UntilAsync(() => notifications.Shown.Count is 1, "the first round alerts", cancellationToken);
		}

		using (monitor.Watch())
		{
			await TestWait.UntilAsync(() => notifications.Shown.Count is 2, "the next round alerts again", cancellationToken);
		}
	}

	[Test]
	public async Task AlertsAgainWhenTheRecordDirectoryChangesWhileLowAsync(CancellationToken cancellationToken)
	{
		FakeConfigManager configManager = new();
		(LowDiskSpaceMonitor monitor, FakeDiskSpace disk, RecordingNotifications notifications, FakeTimeProvider time) = Create(configManager);
		disk.Available = Low;
		using IDisposable watch = monitor.Watch();
		await disk.WaitForCheckAsync(cancellationToken);

		await configManager.UpdateAsync(static config => config.RecordDirectory = "another disk", cancellationToken);
		time.Advance(LowDiskSpaceMonitor.CheckInterval);

		await TestWait.UntilAsync(() => notifications.Shown.Count is 2, "the new record directory is alerted", cancellationToken);
	}

	[Test]
	public async Task UnknownFreeSpaceIsSkippedQuietlyAsync(CancellationToken cancellationToken)
	{
		(LowDiskSpaceMonitor monitor, FakeDiskSpace disk, RecordingNotifications notifications, FakeTimeProvider time) = Create();
		disk.Available = null;
		using IDisposable watch = monitor.Watch();
		await disk.WaitForCheckAsync(cancellationToken);

		for (int i = 0; i < 3; ++i)
		{
			time.Advance(LowDiskSpaceMonitor.CheckInterval);
			await disk.WaitForCheckAsync(cancellationToken);
		}

		await Assert.That(notifications.Shown).IsEmpty();
	}

	[Test]
	public async Task ChecksOnlyWhileSomethingRecordsAsync(CancellationToken cancellationToken)
	{
		(LowDiskSpaceMonitor monitor, FakeDiskSpace disk, _, FakeTimeProvider time) = Create();
		IDisposable first = monitor.Watch();
		IDisposable second = monitor.Watch();
		await disk.WaitForCheckAsync(cancellationToken);

		first.Dispose();
		time.Advance(LowDiskSpaceMonitor.CheckInterval);
		await disk.WaitForCheckAsync(cancellationToken);
		second.Dispose();
		int checks = disk.Checks;

		using (monitor.Watch())
		{
			await disk.WaitForCheckAsync(cancellationToken).WaitAsync(TestWait.DefaultTimeout, TimeProvider.System, cancellationToken);
		}

		await Assert.That(disk.Checks).IsEqualTo(checks + 1);
	}

	[Test]
	public async Task MonitoringDoesNotCarryTheLogScopeOfTheFirstWatcherAsync(CancellationToken cancellationToken)
	{
		ScopeCapturingLogger logger = new();
		(LowDiskSpaceMonitor monitor, FakeDiskSpace disk, _, _) = Create(logger: logger);
		disk.Available = Low;
		IDisposable watch;

		using (logger.BeginScope("first room"))
		{
			watch = monitor.Watch();
		}

		using (watch)
		{
			await Assert.That(await logger.FirstEntryScopes.WaitAsync(cancellationToken)).IsEmpty();
		}
	}

	private static (LowDiskSpaceMonitor, FakeDiskSpace, RecordingNotifications, FakeTimeProvider) Create(FakeConfigManager? config = null, ILogger<LowDiskSpaceMonitor>? logger = null)
	{
		FakeDiskSpace disk = new();
		RecordingNotifications notifications = new();
		FakeTimeProvider time = new();
		return (new LowDiskSpaceMonitor(disk, config ?? new FakeConfigManager(), notifications, time, logger ?? NullLogger<LowDiskSpaceMonitor>.Instance), disk, notifications, time);
	}

	[SuppressMessage("Design", "CA1001", Justification = "只用 WaitAsync 和 Release，不会创建需要释放的等待句柄")]
	private sealed class FakeDiskSpace : IDiskSpaceService
	{
		private readonly SemaphoreSlim _checked = new(0);
		private int _checks;

		public long? Available { get; set; } = Enough;

		public int Checks => Volatile.Read(ref _checks);

		public DiskSpaceInfo? GetDiskSpace(string path)
		{
			DiskSpaceInfo? info = Available is { } available ? new DiskSpaceInfo(100L << 30, available) : null;
			Interlocked.Increment(ref _checks);
			_checked.Release();
			return info;
		}

		public Task WaitForCheckAsync(CancellationToken cancellationToken)
		{
			return _checked.WaitAsync(cancellationToken);
		}
	}

	private sealed class ScopeCapturingLogger : ILogger<LowDiskSpaceMonitor>
	{
		private readonly LoggerExternalScopeProvider _scopes = new();
		private readonly TaskCompletionSource<List<object?>> _firstEntryScopes = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task<List<object?>> FirstEntryScopes => _firstEntryScopes.Task;

		public IDisposable BeginScope<TState>(TState state) where TState : notnull
		{
			return _scopes.Push(state);
		}

		public bool IsEnabled(LogLevel logLevel)
		{
			return true;
		}

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
		{
			List<object?> scopes = [];
			_scopes.ForEachScope(static (scope, list) => list.Add(scope), scopes);
			_firstEntryScopes.TrySetResult(scopes);
		}
	}
}
