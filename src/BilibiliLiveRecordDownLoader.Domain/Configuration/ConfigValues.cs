namespace BilibiliLiveRecordDownLoader.Domain.Configuration;

// ApplyDefaults 在加载和每次修改之后执行；界面要立即显示规范后的值时也调这里
public static class ConfigValues
{
	internal static TimeSpan ClampSplitDuration(TimeSpan duration)
	{
		return Clamp(duration, RecordingSplitDefaults.MinDuration, RecordingSplitDefaults.MaxDuration);
	}

	internal static long ClampSplitSize(long sizeMegabytes)
	{
		return Math.Clamp(sizeMegabytes, (long)RecordingSplitDefaults.MinSizeMegabytes, (long)RecordingSplitDefaults.MaxSizeMegabytes);
	}

	public static TimeSpan ClampDanmakuOffset(TimeSpan offset)
	{
		return Clamp(offset, DanmakuDefaults.MinOffset, DanmakuDefaults.MaxOffset);
	}

	internal static TimeSpan ClampRoomInterval(TimeSpan interval, decimal minimumSeconds)
	{
		return Clamp(interval, TimeSpan.FromSeconds((double)minimumSeconds), TimeSpan.FromSeconds((double)RoomDefaults.MaxIntervalSeconds));
	}

	public static string? NormalizeText(string? value)
	{
		return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
	}

	public static string NormalizeRecordDirectory(string? directory)
	{
		return NormalizeText(directory) ?? ConfigurationConsts.DefaultRecordDirectory;
	}

	private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max)
	{
		return TimeSpan.FromTicks(Math.Clamp(value.Ticks, min.Ticks, max.Ticks));
	}
}
