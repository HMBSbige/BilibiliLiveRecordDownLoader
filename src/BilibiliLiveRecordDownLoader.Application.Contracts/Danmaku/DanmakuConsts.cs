namespace BilibiliLiveRecordDownLoader.Application.Contracts.Danmaku;

public static class DanmakuConsts
{
	public const int MaxFeedEntries = 1000;

	// 弹幕比画面晚到，高能的起因在弹幕变多之前
	public static TimeSpan HighlightExportMargin { get; } = TimeSpan.FromSeconds(15);
}
