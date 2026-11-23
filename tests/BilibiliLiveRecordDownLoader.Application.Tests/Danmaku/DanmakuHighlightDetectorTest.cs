using BilibiliLiveRecordDownLoader.Application.Danmaku;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Danmaku;

public sealed class DanmakuHighlightDetectorTest
{
	private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

	private static List<DanmakuRecord> Background(TimeSpan from, TimeSpan to, int users, long uidBase = 1)
	{
		List<DanmakuRecord> records = [];

		for (int bin = (int)(from.TotalSeconds / 5); bin < (int)(to.TotalSeconds / 5); ++bin)
		{
			for (int user = 0; user < users; ++user)
			{
				records.Add(DanmakuSamples.Comment(bin * 5 + user * 0.01, $@"日常{bin}-{user}", uidBase is 0 ? 0 : uidBase + user));
			}
		}

		return records;
	}

	private static IEnumerable<DanmakuRecord> Burst(double seconds, int users, Func<int, string> text, long uidBase = 100000)
	{
		return Enumerable.Range(0, users).Select(user => (DanmakuRecord)DanmakuSamples.Comment(seconds + user * 0.005, text(user), uidBase is 0 ? 0 : uidBase + user));
	}

	private static string Distinct(int user)
	{
		return $@"不同的话{user}";
	}

	[Test]
	public async Task FindsASpikeAboveTheBaselineAsync()
	{
		List<DanmakuRecord> records = Background(TimeSpan.Zero, Hour, 4);
		records.AddRange(Burst(1800, 40, static user => user % 2 is 0 ? @"草" : Distinct(user)));

		IReadOnlyList<DanmakuHighlight> highlights = DanmakuHighlightDetector.Detect(records, Hour);

		await Assert.That(highlights).IsEquivalentTo([new DanmakuHighlight(TimeSpan.FromSeconds(1800), TimeSpan.FromSeconds(1800), TimeSpan.FromSeconds(1805), 44, @"草")]);
	}

	// 冷清的直播间基线是 1，比例那一关只要 2.5；热度够不上绝对下限时不算高能
	[Test]
	[Arguments(8, new int[0])]
	[Arguments(12, new int[] { 13 })]
	public async Task QuietRoomsNeedTheMinimumScoreAsync(int users, int[] scores)
	{
		List<DanmakuRecord> records = Background(TimeSpan.Zero, Hour, 1);
		records.AddRange(Burst(1800, users, Distinct));

		await Assert.That(DanmakuHighlightDetector.Detect(records, Hour).Select(static highlight => highlight.Score)).IsEquivalentTo(scores);
	}

	[Test]
	public async Task AdjacentHotBinsBecomeOneEventAsync()
	{
		List<DanmakuRecord> records = Background(TimeSpan.Zero, Hour, 4);
		records.AddRange(Burst(1800, 30, Distinct));
		records.AddRange(Burst(1805, 50, Distinct, 200000));
		records.AddRange(Burst(1810, 30, Distinct, 300000));

		DanmakuHighlight highlight = await Assert.That(DanmakuHighlightDetector.Detect(records, Hour)).HasSingleItem();

		await Assert.That((highlight.Start, highlight.Peak, highlight.End, highlight.Score)).IsEqualTo((TimeSpan.FromSeconds(1800), TimeSpan.FromSeconds(1805), TimeSpan.FromSeconds(1815), 54));
	}

	[Test]
	public async Task OneUserFloodingIsNotAHighlightAsync()
	{
		List<DanmakuRecord> records = Background(TimeSpan.Zero, Hour, 4);
		records.AddRange(Enumerable.Range(0, 200).Select(static i => (DanmakuRecord)DanmakuSamples.Comment(1800 + i * 0.02, Distinct(i), 999)));

		await Assert.That(DanmakuHighlightDetector.Detect(records, Hour)).IsEmpty();
	}

	[Test]
	public async Task RhythmStormsAreIgnoredAsync()
	{
		List<DanmakuRecord> records = Background(TimeSpan.Zero, Hour, 4);
		records.AddRange(Burst(1800, 60, static _ => @"节奏风暴口令").Select(static record => (DanmakuRecord)((DanmakuRecord.Comment)record with { IsStorm = true })));

		await Assert.That(DanmakuHighlightDetector.Detect(records, Hour)).IsEmpty();
	}

	[Test]
	public async Task ALotteryPhraseCountsOnlyALittleAsync()
	{
		List<DanmakuRecord> quiet = Background(TimeSpan.Zero, Hour, 4);
		quiet.AddRange(Burst(1800, 60, static _ => @"参与天选 抽奖"));
		List<DanmakuRecord> lively = Background(TimeSpan.Zero, Hour, 4);
		lively.AddRange(Burst(1800, 60, static _ => @"参与天选  抽奖"));
		lively.AddRange(Burst(1800, 30, Distinct, 200000));

		using (Assert.Multiple())
		{
			await Assert.That(DanmakuHighlightDetector.Detect(quiet, Hour)).IsEmpty();
			await Assert.That(DanmakuHighlightDetector.Detect(lively, Hour).Select(static highlight => highlight.Score)).IsEquivalentTo([37]).Because("口令只按 3 个人算");
		}
	}

	[Test]
	public async Task ALotteryPhraseIsNotTheRepresentativePhraseAsync()
	{
		List<DanmakuRecord> records = Background(TimeSpan.Zero, Hour, 4);
		records.AddRange(Burst(1800, 60, static _ => @"参与天选 抽奖"));
		records.AddRange(Burst(1800, 15, static _ => @"好耶", 200000));
		records.AddRange(Burst(1801, 15, Distinct, 300000));

		await Assert.That(DanmakuHighlightDetector.Detect(records, Hour).Select(static highlight => highlight.Phrase)).IsEquivalentTo([@"好耶"]);
	}

	[Test]
	public async Task MaskedUsersAreCountedByTheirHashAsync()
	{
		List<DanmakuRecord> flooding = Background(TimeSpan.Zero, Hour, 4);
		flooding.AddRange(Enumerable.Range(0, 40).Select(static i => (DanmakuRecord)(DanmakuSamples.Comment(1800 + i * 0.01, Distinct(i), 0) with { UidHash = i % 2 is 0 ? @"a16fe0dd" : @"14f71d3" })));
		List<DanmakuRecord> crowd = Background(TimeSpan.Zero, Hour, 4);
		crowd.AddRange(Enumerable.Range(0, 40).Select(static i => (DanmakuRecord)(DanmakuSamples.Comment(1800 + i * 0.01, Distinct(i), 0) with { UidHash = DanmakuUid.Hash(500000 + i) })));

		using (Assert.Multiple())
		{
			await Assert.That(DanmakuHighlightDetector.Detect(flooding, Hour)).IsEmpty();
			await Assert.That(DanmakuHighlightDetector.Detect(crowd, Hour).Select(static highlight => highlight.Score)).IsEquivalentTo([44]);
		}
	}

	[Test]
	public async Task AnonymousMessagesAreCountedOneByOneAsync()
	{
		List<DanmakuRecord> records = Background(TimeSpan.Zero, Hour, 4, 0);
		records.AddRange(Burst(1800, 40, Distinct, 0));

		await Assert.That(DanmakuHighlightDetector.Detect(records, Hour).Select(static highlight => highlight.Score)).IsEquivalentTo([44]);
	}

	// 冷热交界本身也不算高能
	[Test]
	public async Task BaselineFollowsTheRoomActivityAsync()
	{
		List<DanmakuRecord> records = Background(TimeSpan.Zero, TimeSpan.FromMinutes(30), 2);
		records.AddRange(Background(TimeSpan.FromMinutes(30), Hour, 20));
		List<DanmakuRecord> withBursts = [.. records, .. Burst(900, 25, Distinct), .. Burst(2700, 25, Distinct)];

		using (Assert.Multiple())
		{
			await Assert.That(DanmakuHighlightDetector.Detect(records, Hour)).IsEmpty();
			await Assert.That(DanmakuHighlightDetector.Detect(withBursts, Hour).Select(static highlight => highlight.Start)).IsEquivalentTo([TimeSpan.FromSeconds(900)]);
		}
	}

	[Test]
	public async Task OnlyTheHighestWithinThreeMinutesIsKeptAsync()
	{
		List<DanmakuRecord> close = Background(TimeSpan.Zero, Hour, 4);
		close.AddRange(Burst(1800, 40, Distinct));
		close.AddRange(Burst(1860, 50, Distinct));
		List<DanmakuRecord> apart = Background(TimeSpan.Zero, Hour, 4);
		apart.AddRange(Burst(1800, 40, Distinct));
		apart.AddRange(Burst(2040, 50, Distinct));

		using (Assert.Multiple())
		{
			await Assert.That(DanmakuHighlightDetector.Detect(close, Hour).Select(static highlight => highlight.Start)).IsEquivalentTo([TimeSpan.FromSeconds(1860)]);
			await Assert.That(DanmakuHighlightDetector.Detect(apart, Hour).Select(static highlight => highlight.Start)).IsEquivalentTo([TimeSpan.FromSeconds(1800), TimeSpan.FromSeconds(2040)], CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task AtMostSixPerHourKeepingTheHighestAsync()
	{
		List<DanmakuRecord> records = Background(TimeSpan.Zero, Hour, 4);

		for (int k = 0; k < 10; ++k)
		{
			records.AddRange(Burst(300 + k * 300, 20 + k * 2, Distinct, 100000 * (k + 1)));
		}

		IReadOnlyList<DanmakuHighlight> highlights = DanmakuHighlightDetector.Detect(records, Hour);
		IReadOnlyList<DanmakuHighlight> halfHour = DanmakuHighlightDetector.Detect(records.Where(static record => record.Time < TimeSpan.FromMinutes(30)), TimeSpan.FromMinutes(30));

		using (Assert.Multiple())
		{
			await Assert.That(highlights.Select(static highlight => highlight.Start.TotalSeconds)).IsEquivalentTo([1500d, 1800, 2100, 2400, 2700, 3000], CollectionOrdering.Matching);
			await Assert.That(halfHour.Select(static highlight => highlight.Start.TotalSeconds)).IsEquivalentTo([900d, 1200, 1500], CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task RepresentativePhraseIsTheMostCommonShortOneAsync()
	{
		List<DanmakuRecord> laughing = Background(TimeSpan.Zero, Hour, 4);
		laughing.AddRange(Burst(1800, 10, static _ => @"哈哈哈哈"));
		laughing.AddRange(Burst(1801, 8, static _ => @"哈哈哈", 200000));
		laughing.AddRange(Burst(1802, 5, static _ => @"哈哈哈哈哈哈", 300000));
		laughing.AddRange(Burst(1803, 12, static _ => @"好", 400000));
		List<DanmakuRecord> talking = Background(TimeSpan.Zero, Hour, 4);
		talking.AddRange(Burst(1800, 20, static _ => @"这是一句很长很长很长很长很长的完整的话"));
		talking.AddRange(Burst(1801, 12, static _ => @"好", 200000));

		using (Assert.Multiple())
		{
			await Assert.That(DanmakuHighlightDetector.Detect(laughing, Hour).Select(static highlight => highlight.Phrase)).IsEquivalentTo([@"哈哈哈哈"]);
			await Assert.That(DanmakuHighlightDetector.Detect(talking, Hour).Select(static highlight => highlight.Phrase)).IsEquivalentTo([@"好"]);
		}
	}

	[Test]
	public async Task ResultDoesNotDependOnInputOrderAsync()
	{
		List<DanmakuRecord> records = Background(TimeSpan.Zero, Hour, 4);
		records.AddRange(Burst(1200, 30, Distinct));
		records.AddRange(Burst(2400, 40, static user => user % 3 is 0 ? @"好耶" : Distinct(user)));
		DanmakuRecord[] shuffled = [.. records];
		new Random(106).Shuffle(shuffled);

		IReadOnlyList<DanmakuHighlight> ordered = DanmakuHighlightDetector.Detect(records, Hour);

		using (Assert.Multiple())
		{
			await Assert.That(ordered).Count().IsEqualTo(2);
			await Assert.That(DanmakuHighlightDetector.Detect(shuffled, Hour)).IsEquivalentTo(ordered, CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task NoCommentsNoHighlightsAsync()
	{
		await Assert.That(DanmakuHighlightDetector.Detect([new DanmakuRecord.TitleChange(TimeSpan.Zero, @"标题")])).IsEmpty();
	}
}
