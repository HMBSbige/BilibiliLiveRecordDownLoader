namespace BilibiliLiveRecordDownLoader.Domain.Shared.Configuration;

public static class DanmakuDefaults
{
	public const bool IsEnabled = false;

	public const decimal MinOffsetSeconds = -60;

	public const decimal MaxOffsetSeconds = 60;

	public static TimeSpan MinOffset { get; } = TimeSpan.FromSeconds((double)MinOffsetSeconds);

	public static TimeSpan MaxOffset { get; } = TimeSpan.FromSeconds((double)MaxOffsetSeconds);
}
