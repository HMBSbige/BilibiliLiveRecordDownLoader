using BilibiliLiveRecordDownLoader.Application.Danmaku;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Danmaku;

public sealed class DanmakuChapterBuilderTest
{
	private static readonly DanmakuChapterNames Names = new(static title => $@"标题：{title}", static phrase => $@"高能：{phrase}");

	private static DanmakuHighlight Highlight(double startSeconds, string phrase)
	{
		TimeSpan start = TimeSpan.FromSeconds(startSeconds);
		return new DanmakuHighlight(start, start, start + TimeSpan.FromSeconds(5), 50, phrase);
	}

	[Test]
	public async Task StartsWithTheLiveTitleThenTitleChangesAndHighlightsInOrderAsync()
	{
		DanmakuRecord[] records =
		[
			DanmakuSamples.Comment(5),
			new DanmakuRecord.TitleChange(TimeSpan.FromSeconds(600), @"第二个标题"),
			DanmakuSamples.Comment(700)
		];

		IReadOnlyList<FFmpegChapter> chapters = DanmakuChapterBuilder.Build(@"直播标题", records, [Highlight(1210, @"草"), Highlight(300, @"好耶")], TimeSpan.FromSeconds(1800), Names);

		FFmpegChapter[] expected =
		[
			new(TimeSpan.Zero, TimeSpan.FromSeconds(290), @"直播标题"),
			new(TimeSpan.FromSeconds(290), TimeSpan.FromSeconds(600), @"高能：好耶"),
			new(TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(1200), @"标题：第二个标题"),
			new(TimeSpan.FromSeconds(1200), TimeSpan.FromSeconds(1800), @"高能：草")
		];
		await Assert.That(chapters).IsEquivalentTo(expected, CollectionOrdering.Matching);
	}

	[Test]
	public async Task TitleChangesAreFollowedInTimeOrderAsync()
	{
		DanmakuRecord[] records = [new DanmakuRecord.TitleChange(TimeSpan.FromSeconds(200), @"直播标题"), new DanmakuRecord.TitleChange(TimeSpan.FromSeconds(100), @"新标题")];

		IReadOnlyList<FFmpegChapter> chapters = DanmakuChapterBuilder.Build(@"直播标题", records, [], TimeSpan.FromSeconds(300), Names);

		FFmpegChapter[] expected =
		[
			new(TimeSpan.Zero, TimeSpan.FromSeconds(100), @"直播标题"),
			new(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(200), @"标题：新标题"),
			new(TimeSpan.FromSeconds(200), TimeSpan.FromSeconds(300), @"标题：直播标题")
		];
		await Assert.That(chapters).IsEquivalentTo(expected, CollectionOrdering.Matching);
	}

	[Test]
	public async Task UnchangedTitlesDoNotStartAChapterAsync()
	{
		DanmakuRecord[] records = [new DanmakuRecord.TitleChange(TimeSpan.FromSeconds(100), @"直播标题"), new DanmakuRecord.TitleChange(TimeSpan.FromSeconds(200), @"新标题"), new DanmakuRecord.TitleChange(TimeSpan.FromSeconds(300), @"新标题")];

		IReadOnlyList<FFmpegChapter> chapters = DanmakuChapterBuilder.Build(@"直播标题", records, [], TimeSpan.FromSeconds(400), Names);

		await Assert.That(chapters.Select(static chapter => chapter.Title)).IsEquivalentTo([@"直播标题", @"标题：新标题"], CollectionOrdering.Matching);
	}

	[Test]
	public async Task ChaptersTooCloseTogetherAreMergedAsync()
	{
		DanmakuRecord[] records = [new DanmakuRecord.TitleChange(TimeSpan.FromSeconds(100), @"新标题")];

		IReadOnlyList<FFmpegChapter> chapters = DanmakuChapterBuilder.Build(@"直播标题", records, [Highlight(15, @"开场"), Highlight(120, @"草")], TimeSpan.FromSeconds(400), Names);

		FFmpegChapter[] expected =
		[
			new(TimeSpan.Zero, TimeSpan.FromSeconds(100), @"直播标题 / 高能：开场"),
			new(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(400), @"标题：新标题 / 高能：草")
		];
		await Assert.That(chapters).IsEquivalentTo(expected, CollectionOrdering.Matching);
	}

	[Test]
	public async Task NeverMoreThan255ChaptersAsync()
	{
		DanmakuRecord[] records = [.. Enumerable.Range(1, 400).Select(static i => new DanmakuRecord.TitleChange(TimeSpan.FromSeconds(i * 20), $@"标题{i}"))];

		IReadOnlyList<FFmpegChapter> chapters = DanmakuChapterBuilder.Build(@"直播标题", records, [], TimeSpan.FromHours(3), Names);

		using (Assert.Multiple())
		{
			await Assert.That(chapters).Count().IsEqualTo(DanmakuChapterBuilder.MaxChapters);
			await Assert.That(chapters[^1]).IsEqualTo(new FFmpegChapter(TimeSpan.FromSeconds(254 * 20), TimeSpan.FromHours(3), @"标题：标题254"));
		}
	}

	[Test]
	public async Task ChaptersAfterTheEndAreDroppedAsync()
	{
		DanmakuRecord[] records = [new DanmakuRecord.TitleChange(TimeSpan.FromSeconds(500), @"太晚了")];

		IReadOnlyList<FFmpegChapter> chapters = DanmakuChapterBuilder.Build(@"直播标题", records, [Highlight(600, @"也太晚了")], TimeSpan.FromSeconds(400), Names);

		await Assert.That(chapters).IsEquivalentTo([new FFmpegChapter(TimeSpan.Zero, TimeSpan.FromSeconds(400), @"直播标题")]);
	}

	[Test]
	public async Task WithoutADurationTheLastChapterEndsAfterTheLastRecordAsync()
	{
		IReadOnlyList<FFmpegChapter> chapters = DanmakuChapterBuilder.Build(@"直播标题", [DanmakuSamples.Comment(42)], [], null, Names);

		await Assert.That(chapters).IsEquivalentTo([new FFmpegChapter(TimeSpan.Zero, TimeSpan.FromSeconds(43), @"直播标题")]);
	}
}
