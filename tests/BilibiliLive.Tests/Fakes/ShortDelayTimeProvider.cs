using Microsoft.Extensions.Time.Testing;
using System.Collections.Concurrent;

namespace BilibiliLive.Tests.Fakes;

/// <summary>One-shot timers shorter than <paramref name="longest"/> fire at once, so it must not exceed the timeouts a test controls with <see cref="Advance"/>.</summary>
internal sealed class ShortDelayTimeProvider(TimeSpan longest) : TimeProvider
{
	private readonly FakeTimeProvider _clock = new();

	public ConcurrentQueue<TimeSpan> Delays { get; } = new();

	public override long TimestampFrequency => _clock.TimestampFrequency;

	public override DateTimeOffset GetUtcNow()
	{
		return _clock.GetUtcNow();
	}

	public override long GetTimestamp()
	{
		return _clock.GetTimestamp();
	}

	public void Advance(TimeSpan delta)
	{
		_clock.Advance(delta);
	}

	public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
	{
		if (dueTime >= longest || dueTime == Timeout.InfiniteTimeSpan || period != Timeout.InfiniteTimeSpan)
		{
			return _clock.CreateTimer(callback, state, dueTime, period);
		}

		Delays.Enqueue(dueTime);
		ThreadPool.QueueUserWorkItem(static timer => timer.callback(timer.state), (callback, state), preferLocal: false);

		// A later Change, such as CancelAfter moving a deadline, then follows the fake clock
		return _clock.CreateTimer(callback, state, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
	}
}
