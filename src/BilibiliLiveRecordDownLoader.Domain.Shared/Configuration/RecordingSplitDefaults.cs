namespace BilibiliLiveRecordDownLoader.Domain.Shared.Configuration;

public static class RecordingSplitDefaults
{
	// 上下限以界面的单位表示，数值输入框直接引用
	public const decimal MinDurationMinutes = 1;

	public const decimal MaxDurationMinutes = 24 * 60;

	public const decimal MinSizeMegabytes = 64;

	public const decimal MaxSizeMegabytes = 1 << 20;

	public const long SizeMegabytes = 4000;

	public static TimeSpan Duration { get; } = TimeSpan.FromMinutes(60);

	public static TimeSpan MinDuration { get; } = TimeSpan.FromMinutes((double)MinDurationMinutes);

	public static TimeSpan MaxDuration { get; } = TimeSpan.FromMinutes((double)MaxDurationMinutes);
}
