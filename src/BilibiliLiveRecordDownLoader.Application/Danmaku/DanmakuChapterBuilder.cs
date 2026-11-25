namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

internal sealed record DanmakuChapterNames(Func<string, string> TitleChange, Func<string, string> Highlight);

internal static class DanmakuChapterBuilder
{
	// MP4 的 Nero chpl 盒子最多 255 个章节
	public const int MaxChapters = 255;

	private const string MergedSeparator = @" / ";

	private static readonly TimeSpan HighlightLead = TimeSpan.FromSeconds(10);

	private static readonly TimeSpan MinimumSpacing = TimeSpan.FromSeconds(15);

	// duration 为空时最后一章结束于最后一条记录之后一秒
	public static IReadOnlyList<MediaChapter> Build(string liveTitle, IEnumerable<DanmakuRecord> records, IEnumerable<DanmakuHighlight> highlights, TimeSpan? duration, DanmakuChapterNames names)
	{
		List<DanmakuRecord.TitleChange> titleChanges = [];
		TimeSpan lastRecord = TimeSpan.Zero;

		foreach (DanmakuRecord record in records)
		{
			lastRecord = record.Time > lastRecord ? record.Time : lastRecord;

			if (record is DanmakuRecord.TitleChange change)
			{
				titleChanges.Add(change);
			}
		}

		List<(TimeSpan Start, string Title)> candidates = [];
		string currentTitle = liveTitle;

		// OrderBy 是稳定排序，同一时刻的几次改标题按文件里的先后去重
		foreach (DanmakuRecord.TitleChange change in titleChanges.OrderBy(static change => change.Time))
		{
			if (string.Equals(change.Title, currentTitle, StringComparison.Ordinal))
			{
				continue;
			}

			currentTitle = change.Title;

			if (duration is not { } end || change.Time < end)
			{
				candidates.Add((change.Time, names.TitleChange(change.Title)));
			}
		}

		foreach (DanmakuHighlight highlight in highlights)
		{
			TimeSpan earlier = highlight.Start - HighlightLead;
			TimeSpan start = earlier > TimeSpan.Zero ? earlier : TimeSpan.Zero;

			if (duration is not { } end || start < end)
			{
				candidates.Add((start, names.Highlight(highlight.Phrase)));
			}
		}

		List<(TimeSpan Start, string Title)> chapters = [(TimeSpan.Zero, liveTitle)];

		foreach ((TimeSpan start, string title) in candidates.OrderBy(static candidate => candidate.Start))
		{
			(TimeSpan previousStart, string previousTitle) = chapters[^1];

			if (start - previousStart < MinimumSpacing)
			{
				chapters[^1] = (previousStart, previousTitle + MergedSeparator + title);
			}
			else if (chapters.Count < MaxChapters)
			{
				chapters.Add((start, title));
			}
		}

		TimeSpan finish = duration ?? lastRecord + TimeSpan.FromSeconds(1);
		MediaChapter[] result = new MediaChapter[chapters.Count];

		for (int i = 0; i < chapters.Count; ++i)
		{
			TimeSpan end = i + 1 < chapters.Count ? chapters[i + 1].Start : finish;
			result[i] = new MediaChapter(chapters[i].Start, end > chapters[i].Start ? end : chapters[i].Start + TimeSpan.FromMilliseconds(1), chapters[i].Title);
		}

		return result;
	}
}
