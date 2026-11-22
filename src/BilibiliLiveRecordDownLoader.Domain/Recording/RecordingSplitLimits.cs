namespace BilibiliLiveRecordDownLoader.Domain.Recording;

internal static class RecordingSplitLimits
{
	public static TimeSpan ClampDuration(TimeSpan duration)
	{
		return TimeSpan.FromTicks(Math.Clamp(duration.Ticks, RecordingSplitDefaults.MinDuration.Ticks, RecordingSplitDefaults.MaxDuration.Ticks));
	}

	public static long ClampSize(long sizeMegabytes)
	{
		return Math.Clamp(sizeMegabytes, (long)RecordingSplitDefaults.MinSizeMegabytes, (long)RecordingSplitDefaults.MaxSizeMegabytes);
	}
}
