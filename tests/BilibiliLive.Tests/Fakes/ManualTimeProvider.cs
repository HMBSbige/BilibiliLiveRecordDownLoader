using System.Collections.Concurrent;

namespace BilibiliLive.Tests.Fakes;

// FakeTimeProvider 的时钟不能往回调；计时器立即触发，只记下本该等待多久
internal sealed class ManualTimeProvider : TimeProvider
{
	private DateTimeOffset _now = DateTimeOffset.FromUnixTimeSeconds(1702204169);

	public ConcurrentQueue<TimeSpan> Delays { get; } = new();

	public override DateTimeOffset GetUtcNow()
	{
		return _now;
	}

	public void Advance(TimeSpan delta)
	{
		_now += delta;
	}

	public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
	{
		Delays.Enqueue(dueTime);
		ThreadPool.QueueUserWorkItem(static timer => timer.callback(timer.state), (callback, state), preferLocal: false);
		return new NoopTimer();
	}

	private sealed class NoopTimer : ITimer
	{
		public bool Change(TimeSpan dueTime, TimeSpan period)
		{
			return true;
		}

		public void Dispose()
		{
		}

		public ValueTask DisposeAsync()
		{
			return ValueTask.CompletedTask;
		}
	}
}
