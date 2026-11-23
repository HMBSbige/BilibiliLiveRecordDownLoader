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
	public static IReadOnlyList<FFmpegChapter> Build(string liveTitle, IEnumerable<DanmakuRecord> records, IEnumerable<DanmakuHighlight> highlights, TimeSpan? duration, DanmakuChapterNames names)
	{
		List<(TimeSpan Start, string Title)> candidates = [];
		TimeSpan lastRecord = TimeSpan.Zero;
		string currentTitle = liveTitle;

		foreach (DanmakuRecord record in records.OrderBy(static record => record.Time))
		{
			lastRecord = record.Time > lastRecord ? record.Time : lastRecord;

			if (record is DanmakuRecord.TitleChange change && !string.Equals(change.Title, currentTitle, StringComparison.Ordinal))
			{
				currentTitle = change.Title;
				candidates.Add((change.Time, names.TitleChange(change.Title)));
			}
		}

		foreach (DanmakuHighlight highlight in highlights)
		{
			TimeSpan start = highlight.Start - HighlightLead;
			candidates.Add((start > TimeSpan.Zero ? start : TimeSpan.Zero, names.Highlight(highlight.Phrase)));
		}

		List<(TimeSpan Start, string Title)> chapters = [(TimeSpan.Zero, liveTitle)];

		foreach ((TimeSpan start, string title) in candidates.Where(candidate => duration is not { } end || candidate.Start < end).OrderBy(static candidate => candidate.Start))
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
		FFmpegChapter[] result = new FFmpegChapter[chapters.Count];

		for (int i = 0; i < chapters.Count; ++i)
		{
			TimeSpan end = i + 1 < chapters.Count ? chapters[i + 1].Start : finish;
			result[i] = new FFmpegChapter(chapters[i].Start, end > chapters[i].Start ? end : chapters[i].Start + TimeSpan.FromMilliseconds(1), chapters[i].Title);
		}

		return result;
	}
}
