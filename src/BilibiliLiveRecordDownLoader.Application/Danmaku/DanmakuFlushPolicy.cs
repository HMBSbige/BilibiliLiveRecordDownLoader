namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

internal sealed record DanmakuFlushPolicy
{
	public DanmakuFlushPolicy(TimeSpan interval, int maxPending)
	{
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
		ArgumentOutOfRangeException.ThrowIfLessThan(maxPending, 1);
		Interval = interval;
		MaxPending = maxPending;
	}

	public static DanmakuFlushPolicy Default { get; } = new(TimeSpan.FromSeconds(5), 500);

	public TimeSpan Interval { get; }

	public int MaxPending { get; }

	public TimeSpan GetFlushDueIn(TimeSpan oldestPendingAge)
	{
		return oldestPendingAge < Interval ? Interval - oldestPendingAge : TimeSpan.Zero;
	}
}
