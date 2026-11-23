using BilibiliLiveRecordDownLoader.Application.Contracts.Storage;
using BilibiliLiveRecordDownLoader.Application.Storage;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using Microsoft.Extensions.Logging;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class LowDiskSpaceMonitorTest
{
	private const long Low = LowDiskSpaceMonitor.Threshold - 1;
	private const long Enough = LowDiskSpaceMonitor.Threshold;

	[Test]
	[Timeout(10_000)]
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

	[Test]
	[Timeout(10_000)]
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
	[Timeout(10_000)]
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

		time.Advance(LowDiskSpaceMonitor.CheckInterval * 3);
		await Task.Delay(100, cancellationToken);

		await Assert.That(disk.Checks).IsEqualTo(checks);
	}

	[Test]
	[Timeout(10_000)]
	public async Task MonitoringDoesNotCarryTheLogScopeOfTheFirstWatcherAsync(CancellationToken cancellationToken)
	{
		ScopeCapturingLogger logger = new();
		(LowDiskSpaceMonitor monitor, FakeDiskSpace disk, _, _) = Create(logger);
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

	private static (LowDiskSpaceMonitor, FakeDiskSpace, RecordingNotifications, FakeTimeProvider) Create(ILogger<LowDiskSpaceMonitor>? logger = null)
	{
		FakeDiskSpace disk = new();
		RecordingNotifications notifications = new();
		FakeTimeProvider time = new();
		return (new LowDiskSpaceMonitor(disk, new FakeConfigManager(), notifications, time, logger ?? NullLogger<LowDiskSpaceMonitor>.Instance), disk, notifications, time);
	}

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

	// 作用域和 Microsoft.Extensions.Logging 一样随 ExecutionContext 流动
	private sealed class ScopeCapturingLogger : ILogger<LowDiskSpaceMonitor>
	{
		private readonly LoggerExternalScopeProvider _scopes = new();
		private readonly TaskCompletionSource<List<object?>> _firstEntryScopes = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task<List<object?>> FirstEntryScopes => _firstEntryScopes.Task;

		public IDisposable? BeginScope<TState>(TState state) where TState : notnull
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
