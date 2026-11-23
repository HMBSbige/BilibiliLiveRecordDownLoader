namespace BilibiliLiveRecordDownLoader.Domain.Shared.Configuration;

// ApplyDefaults 在加载和每次修改之后执行；界面要立即显示规范后的值时也调这里
public static class ConfigValues
{
	public static TimeSpan ClampSplitDuration(TimeSpan duration)
	{
		return Clamp(duration, RecordingSplitDefaults.MinDuration, RecordingSplitDefaults.MaxDuration);
	}

	public static long ClampSplitSize(long sizeMegabytes)
	{
		return Math.Clamp(sizeMegabytes, (long)RecordingSplitDefaults.MinSizeMegabytes, (long)RecordingSplitDefaults.MaxSizeMegabytes);
	}

	// 弹幕文件头的偏移按毫秒截断写入，偏移不是整毫秒时会和记录实际平移的量差 1 毫秒
	public static TimeSpan ClampDanmakuOffset(TimeSpan offset)
	{
		return FromMilliseconds((decimal)Clamp(offset, DanmakuDefaults.MinOffset, DanmakuDefaults.MaxOffset).Ticks / TimeSpan.TicksPerMillisecond);
	}

	// 不经过 double：53.669 这类值转成 double 后略小，换算结果会落在整毫秒之下
	public static TimeSpan DanmakuOffsetFromSeconds(decimal seconds)
	{
		return FromMilliseconds(seconds * 1000);
	}

	public static TimeSpan ClampRoomInterval(TimeSpan interval, decimal minimumSeconds)
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

	// 和界面 "0.###" 显示时的舍入一致
	private static TimeSpan FromMilliseconds(decimal milliseconds)
	{
		return TimeSpan.FromMilliseconds((long)decimal.Round(milliseconds, MidpointRounding.AwayFromZero));
	}
}
