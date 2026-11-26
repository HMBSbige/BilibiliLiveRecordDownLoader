using System.Diagnostics;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

internal sealed class DelayTrackingTimeProvider : FakeTimeProvider
{
	private readonly Lock _lock = new();
	private readonly List<PendingDelay> _pending = [];

	public async Task<PendingDelay> WaitForDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
	{
		PendingDelay? found = null;
		await TestWait.UntilAsync(() => (found = Find(delay)) is not null, $"a {delay} delay starts", cancellationToken);
		return found ?? throw new UnreachableException();
	}

	// 别的线程正在触发计时器时，Advance 把到期回调留给那个线程就返回
	public Task AdvanceThroughAsync(PendingDelay delay, CancellationToken cancellationToken)
	{
		Advance(delay.DueTime);
		return TestWait.ForAsync(delay.Ended, $"the {delay.DueTime} delay ends", cancellationToken);
	}

	public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
	{
		// 周期计时器一直都在，说明不了正在等什么
		if (period != Timeout.InfiniteTimeSpan || dueTime == Timeout.InfiniteTimeSpan)
		{
			return base.CreateTimer(callback, state, dueTime, period);
		}

		PendingDelay delay = new(dueTime);
		ITimer timer = base.CreateTimer(s =>
		{
			End(delay);
			callback(s);
		}, state, dueTime, period);

		// 注册之后才能让测试看到，否则推进可能落在注册之前
		lock (_lock)
		{
			if (!delay.Ended.IsCompleted)
			{
				_pending.Add(delay);
			}
		}

		return new TrackedTimer(timer, () => End(delay));
	}

	private PendingDelay? Find(TimeSpan dueTime)
	{
		lock (_lock)
		{
			return _pending.Find(delay => delay.DueTime == dueTime);
		}
	}

	private void End(PendingDelay delay)
	{
		lock (_lock)
		{
			_pending.Remove(delay);
			delay.MarkEnded();
		}
	}

	private sealed class TrackedTimer(ITimer inner, Action end) : ITimer
	{
		public bool Change(TimeSpan dueTime, TimeSpan period)
		{
			end();
			return inner.Change(dueTime, period);
		}

		public void Dispose()
		{
			end();
			inner.Dispose();
		}

		public ValueTask DisposeAsync()
		{
			end();
			return inner.DisposeAsync();
		}
	}
}

internal sealed class PendingDelay(TimeSpan dueTime)
{
	private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public TimeSpan DueTime { get; } = dueTime;

	public Task Ended => _ended.Task;

	public void MarkEnded()
	{
		_ended.TrySetResult();
	}
}
