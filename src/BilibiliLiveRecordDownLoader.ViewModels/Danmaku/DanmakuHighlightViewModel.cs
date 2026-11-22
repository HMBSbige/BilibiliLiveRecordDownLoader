namespace BilibiliLiveRecordDownLoader.ViewModels.Danmaku;

public sealed class DanmakuHighlightViewModel(DanmakuHighlightDto highlight)
{
	public DanmakuHighlightDto Highlight { get; } = highlight;

	public TimeSpan Start => Highlight.Start;

	public TimeSpan End => Highlight.End;

	public TimeSpan Peak => Highlight.Peak;

	public string TimeText => DanmakuEntryViewModel.FormatTime(Highlight.Start);

	public string Phrase => string.IsNullOrWhiteSpace(Highlight.Phrase) ? AppLanguage.Current.ChapterHighlightWithoutPhrase : Highlight.Phrase;
}
