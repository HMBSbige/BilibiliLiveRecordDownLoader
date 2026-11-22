namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

internal static class DanmakuTexts
{
	public static DanmakuAssLabels SubtitleLabels => new
	(
		static (user, level, months, isRenewal) => $"{user} {DanmakuGuardText.Format(level, months, isRenewal)}",
		static (user, gift, count) => $"{user} {AppLanguage.Current.DanmakuGiftText.ToString(gift, count)}"
	);

	public static DanmakuChapterNames ChapterNames => new(static title => AppLanguage.Current.ChapterTitleChange.ToString(title), static phrase => phrase.Length is 0 ? AppLanguage.Current.ChapterHighlightWithoutPhrase : AppLanguage.Current.ChapterHighlight.ToString(phrase));
}
