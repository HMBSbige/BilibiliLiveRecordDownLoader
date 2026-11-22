namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// PTS 只有 33 位，90 kHz 下约 26.5 小时回绕一次；按和上一个值相差不到半个周期展开
// 展开结果取决于第一个值和之后的顺序，要比较的两个时间得由喂同样的值的两个实例展开
internal struct PtsClock
{
	public const long Period = 1L << 33;
	public const long ClockRate = 90_000;

	private long? _last;

	public TimeSpan Unwrap(long pts)
	{
		return ToTime(Extend(pts));
	}

	public static TimeSpan ToTime(long value)
	{
		return TimeSpan.FromTicks(value * TimeSpan.TicksPerSecond / ClockRate);
	}

	// 仍按 90 kHz 计
	public long Extend(long pts)
	{
		long unwrapped = _last is { } last ? Near(last, pts) : pts;
		_last = unwrapped;
		return unwrapped;
	}

	// 和 reference 模 2³³ 同余、相差在半个周期以内的值；reference 是展开过的值
	public static long Near(long reference, long pts)
	{
		long delta = (pts - reference) % Period;

		if (delta < 0)
		{
			delta += Period;
		}

		if (delta >= Period / 2)
		{
			delta -= Period;
		}

		return reference + delta;
	}
}
