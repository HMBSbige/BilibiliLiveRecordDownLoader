namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// PTS 只有 33 位，90 kHz 下约 26.5 小时回绕一次；按和上一个值的差在半个周期以内展开成连续的时间
internal struct PtsClock
{
	private const long Period = 1L << 33;
	private const long ClockRate = 90_000;

	private long? _last;

	public TimeSpan Unwrap(long pts)
	{
		long unwrapped = pts;

		if (_last is { } last)
		{
			long delta = (pts - last) % Period;

			if (delta < 0)
			{
				delta += Period;
			}

			if (delta >= Period / 2)
			{
				delta -= Period;
			}

			unwrapped = last + delta;
		}

		_last = unwrapped;
		return TimeSpan.FromTicks(unwrapped * TimeSpan.TicksPerSecond / ClockRate);
	}
}
