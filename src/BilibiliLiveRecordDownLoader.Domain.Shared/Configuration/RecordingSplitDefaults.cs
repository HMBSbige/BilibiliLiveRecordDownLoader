namespace BilibiliLiveRecordDownLoader.Domain.Shared.Configuration;

public static class RecordingSplitDefaults
{
	public const decimal MinDurationMinutes = 1;

	public const decimal MaxDurationMinutes = 24 * 60;

	public const decimal MinSizeMebibytes = 64;

	public const decimal MaxSizeMebibytes = 1 << 20;

	public const long SizeMebibytes = 4000;

	public static TimeSpan Duration { get; } = TimeSpan.FromMinutes(60);

	public static TimeSpan MinDuration { get; } = TimeSpan.FromMinutes((double)MinDurationMinutes);

	public static TimeSpan MaxDuration { get; } = TimeSpan.FromMinutes((double)MaxDurationMinutes);
}
