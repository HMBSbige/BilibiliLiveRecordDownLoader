using System.Collections.Concurrent;

namespace BilibiliLive.Tests.Fakes;

/// <summary>A clock that only moves when told to, and whose timers fire at once while recording the requested delay.</summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
	private DateTimeOffset _now = start;

	public ConcurrentQueue<TimeSpan> Delays { get; } = new();

	public ManualTimeProvider() : this(DateTimeOffset.FromUnixTimeSeconds(1702204169))
	{
	}

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
		ThreadPool.QueueUserWorkItem(_ => callback(state));
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
