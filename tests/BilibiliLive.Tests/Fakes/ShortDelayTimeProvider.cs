using Microsoft.Extensions.Time.Testing;
using System.Collections.Concurrent;

namespace BilibiliLive.Tests.Fakes;

/// <summary>短于 <paramref name="longest"/> 的一次性计时器立即触发，所以它不能超过测试用 <see cref="Advance"/> 控制的超时</summary>
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

		// 之后的 Change（例如 CancelAfter 推迟截止时间）跟随假时钟
		return _clock.CreateTimer(callback, state, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
	}
}
