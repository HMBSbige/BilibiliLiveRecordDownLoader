using System.Collections.Concurrent;

namespace BilibiliLive.Tests.Fakes;

// FakeTimeProvider 的时钟不能往回调；计时器在创建时同步触发，只记下本该等待多久
// 不排进线程池：并行测试阻塞线程池时，全局队列里的回调可能等过测试超时
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
		callback(state);
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
