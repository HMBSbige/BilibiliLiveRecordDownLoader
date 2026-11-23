using BilibiliLive.Danmu;
using BilibiliLiveRecordDownLoader.Application.Contracts.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Recording;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Danmaku;

public sealed partial class DanmakuAssMessagesTest
{
	private static readonly DanmakuAssLabels Labels = new
	(
		static (user, level, months, isRenewal) => $@"{user} {(isRenewal ? @"续费了" : @"开通了")}{level switch { GuardLevel.Governor => @"总督", GuardLevel.Admiral => @"提督", GuardLevel.Captain => @"舰长", _ => throw new UnreachableException() }}{(months > 1 ? $@"×{months}个月" : string.Empty)}",
		static (user, gift, count) => $@"{user} 投喂 {gift} ×{count}"
	);

	[GeneratedRegex(@"^Dialogue: (?<layer>\d),(?<start>[^,]+),(?<end>[^,]+),Message,,0,0,0,,\{(?<tags>[^}]*)\}(?<text>.*)$")]
	private static partial Regex MessageRegex { get; }

	private static (Match[] Messages, DanmakuAssResult Result, string Text) Generate(IEnumerable<DanmakuRecord> records, VideoResolution? size = null)
	{
		StringWriter writer = new(CultureInfo.InvariantCulture);
		DanmakuAssResult result = DanmakuAssGenerator.Write(writer, records, size, Labels);
		string text = writer.ToString();
		return ([.. text.Split('\n').Select(static line => MessageRegex.Match(line)).Where(static match => match.Success)], result, text);
	}

	private static string[] Texts(Match[] messages)
	{
		return [.. messages.Where(static match => match.Groups[@"layer"].Value is "2").Select(static match => match.Groups[@"text"].Value)];
	}

	private static IEnumerable<string> ShapeTags(Match[] messages)
	{
		return messages.Where(static match => match.Groups[@"layer"].Value is "1").Select(static match => match.Groups[@"tags"].Value);
	}

	private static string Color(int rgb)
	{
		return $@"\1c&H{DanmakuAssText.Color(rgb)}&";
	}

	// B 站原样的价格色在浅色底上看不清
	[Test]
	public async Task SuperChatCardTakesItsColorsFromTheTierAsync()
	{
		(Match[] messages, DanmakuAssResult result, _) = Generate([new DanmakuRecord.SuperChat(TimeSpan.FromSeconds(1), 1, @"老板", 100_000, TimeSpan.FromSeconds(60), @"醒目留言")]);
		SuperChatTier tier = SuperChatTier.For(100_000);
		Dictionary<string, string> texts = messages.Where(static match => match.Groups[@"layer"].Value is "2").ToDictionary(static match => match.Groups[@"text"].Value, static match => match.Groups[@"tags"].Value);

		using (Assert.Multiple())
		{
			await Assert.That(result).IsEqualTo(new DanmakuAssResult(0, 0, 1, 0));
			// 底边离画面底部 4%，左边离画面左边 2%
			await Assert.That(ShapeTags(messages)).IsEquivalentTo([$@"\an7\pos(38,933)\fad(200,200){Color(tier.Background)}\1a&H00&\p1", $@"\an7\pos(38,981)\fad(200,200){Color(tier.BottomBackground)}\1a&H00&\p1"], CollectionOrdering.Matching);
			await Assert.That(texts[@"老板"]).Contains($@"\b1{Color(0x333333)}");
			await Assert.That(texts[@"¥100"]).StartsWith(@"\an6\pos(502,957)").And.EndsWith(Color(tier.PriceColor));
			await Assert.That(texts[@"醒目留言"]).EndsWith(Color(tier.TextColor));
		}
	}

	[Test]
	public async Task EverythingIsOnTheRightLayerAsync()
	{
		(Match[] messages, _, string text) = Generate([DanmakuSamples.Comment(0), new DanmakuRecord.SuperChat(TimeSpan.Zero, 1, @"老板", 30_000, TimeSpan.FromSeconds(60), @"内容"), new DanmakuRecord.Guard(TimeSpan.Zero, 2, @"舰长", GuardLevel.Captain, 1, 198_000), new DanmakuRecord.Gift(TimeSpan.Zero, 3, @"观众", @"礼物", 1, 1000)]);

		using (Assert.Multiple())
		{
			await Assert.That(text).Contains(@"Dialogue: 0,0:00:00.00,0:00:12.00,Danmaku,");
			await Assert.That(messages).All(static match => match.Groups[@"layer"].Value is "1" == match.Groups[@"tags"].Value.EndsWith(@"\p1", StringComparison.Ordinal));
			await Assert.That(Texts(messages).Distinct()).IsEquivalentTo([@"老板", @"¥30", @"内容", @"舰长 开通了舰长", @"观众 投喂 礼物 ×1"]);
		}
	}

	// 没记 id 的旧记录对不上删除，照常画
	[Test]
	public async Task DeletedSuperChatsAreNotDrawnAsync()
	{
		(Match[] messages, DanmakuAssResult result, _) = Generate(
		[
			new DanmakuRecord.SuperChat(TimeSpan.FromSeconds(1), 1, @"甲", 30_000, TimeSpan.FromSeconds(60), @"被删") { Id = 7 },
			new DanmakuRecord.SuperChat(TimeSpan.FromSeconds(2), 2, @"乙", 30_000, TimeSpan.FromSeconds(60), @"留着") { Id = 8 },
			new DanmakuRecord.SuperChat(TimeSpan.FromSeconds(3), 3, @"丙", 30_000, TimeSpan.FromSeconds(60), @"没有 id"),
			new DanmakuRecord.SuperChatDeletion(TimeSpan.FromSeconds(30), [7, 0])
		]);
		string[] texts = Texts(messages);

		using (Assert.Multiple())
		{
			await Assert.That(texts).Contains(@"留着");
			await Assert.That(texts).Contains(@"没有 id");
			await Assert.That(texts).DoesNotContain(@"被删");
			await Assert.That(texts).DoesNotContain(@"甲");
			await Assert.That(result.MessagesShown).IsEqualTo(2);
		}
	}

	[Test]
	public async Task GuardsShowTheLevelAndMonthsAsync()
	{
		(Match[] messages, DanmakuAssResult result, _) = Generate(
		[
			new DanmakuRecord.Guard(TimeSpan.Zero, 1, @"甲", GuardLevel.Admiral, 3, 1_998_000 * 3),
			new DanmakuRecord.Guard(TimeSpan.Zero, 2, @"乙", GuardLevel.Governor, 1, 19_998_000),
			new DanmakuRecord.Guard(TimeSpan.Zero, 3, @"丙", GuardLevel.Captain, 1, 198_000) { IsRenewal = true },
			// 直播消息里也可能带来没定义的等级
			new DanmakuRecord.Guard(TimeSpan.Zero, 4, @"丁", GuardLevel.None, 1, 198_000),
			new DanmakuRecord.Guard(TimeSpan.Zero, 5, @"戊", (GuardLevel)7, 1, 198_000)
		]);

		using (Assert.Multiple())
		{
			await Assert.That(Texts(messages)).IsEquivalentTo([@"甲 开通了提督×3个月", @"乙 开通了总督", @"丙 续费了舰长"]);
			await Assert.That(result).IsEqualTo(new DanmakuAssResult(0, 0, 3, 0));
		}
	}

	[Test]
	[Arguments(GuardLevel.Governor, 0xE0A030)]
	[Arguments(GuardLevel.Admiral, 0x8C4DE6)]
	[Arguments(GuardLevel.Captain, 0x3E7BFA)]
	public async Task EachGuardLevelUsesItsOwnColorAsync(GuardLevel level, int color)
	{
		(Match[] messages, _, _) = Generate([new DanmakuRecord.Guard(TimeSpan.Zero, 1, @"甲", level, 1, 198_000)]);
		string shape = await Assert.That(ShapeTags(messages)).HasSingleItem();

		await Assert.That(shape).Contains(Color(color));
	}

	[Test]
	public async Task OnlyPaidGiftsAreShownAndCombosMergeByUidOrByNameWhenMaskedAsync()
	{
		(Match[] messages, DanmakuAssResult result, _) = Generate(
		[
			new DanmakuRecord.Gift(TimeSpan.Zero, 0, @"淡***", @"小心心", 1, 0),
			new DanmakuRecord.Gift(TimeSpan.FromSeconds(1), 7, @"甲", @"人气票", 1, 1000),
			new DanmakuRecord.Gift(TimeSpan.FromSeconds(2), 7, @"甲改名了", @"人气票", 2, 2000),
			new DanmakuRecord.Gift(TimeSpan.FromSeconds(3), 0, @"淡***", @"人气票", 1, 1000),
			new DanmakuRecord.Gift(TimeSpan.FromSeconds(3.5), 0, @"淡***", @"人气票", 1, 1000),
			new DanmakuRecord.Gift(TimeSpan.FromSeconds(4), 0, @"浓***", @"人气票", 1, 1000)
		]);
		string[] texts = [.. Texts(messages).Distinct()];

		using (Assert.Multiple())
		{
			await Assert.That(texts).Contains(@"甲 投喂 人气票 ×3");
			await Assert.That(texts).Contains(@"淡*** 投喂 人气票 ×2");
			await Assert.That(texts).Contains(@"浓*** 投喂 人气票 ×1");
			await Assert.That(texts).DoesNotContain(static text => text.Contains(@"小心心", StringComparison.Ordinal));
			await Assert.That(result).IsEqualTo(new DanmakuAssResult(0, 0, 5, 0));
		}
	}

	[Test]
	public async Task NamesGiftsAndTextAreEscapedAsync()
	{
		(Match[] messages, _, _) = Generate(
		[
			new DanmakuRecord.SuperChat(TimeSpan.Zero, 1, @"{\b1}", 30_000, TimeSpan.FromSeconds(60), "第一行\n{\\N}"),
			new DanmakuRecord.Gift(TimeSpan.Zero, 2, @"观众", @"{礼物}", 1, 1000)
		]);

		await Assert.That(Texts(messages).Distinct()).IsEquivalentTo([@"｛＼b1｝", @"¥30", @"第一行 ｛＼N｝", @"观众 投喂 ｛礼物｝ ×1"]);
	}

	// 8 秒起，每个字加 0.15 秒，最多 20 秒，但不超过 SC 在直播间里置顶的时长
	[Test]
	[Arguments(0, 60, 8)]
	[Arguments(20, 60, 11)]
	[Arguments(100, 60, 20)]
	[Arguments(2, 5, 5)]
	public async Task SuperChatsStayLongerForLongerTextButNotPastTheirOwnDurationAsync(int length, int duration, double seconds)
	{
		(Match[] messages, _, _) = Generate([new DanmakuRecord.SuperChat(TimeSpan.FromSeconds(1), 1, @"老板", 30_000, TimeSpan.FromSeconds(duration), new string('字', length))]);

		await Assert.That(messages.Select(static match => match.Groups[@"end"].Value).Distinct()).IsEquivalentTo([DanmakuAssText.Time(TimeSpan.FromSeconds(1 + seconds))]);
	}

	[Test]
	public async Task OlderEntriesMoveUpAndSplitIntoSegmentsAsync()
	{
		(Match[] messages, _, _) = Generate([new DanmakuRecord.Guard(TimeSpan.Zero, 1, @"甲", GuardLevel.Captain, 1, 198_000), new DanmakuRecord.Guard(TimeSpan.FromSeconds(1), 2, @"乙", GuardLevel.Captain, 1, 198_000)]);

		// 胶囊高 46、间距 8；1080 × 0.96 - 46 = 990.8，取整 991；中线再往下 23
		await Assert.That(messages.Where(static match => match.Groups[@"text"].Value is @"甲 开通了舰长").Select(static match => $"{match.Groups[@"start"].Value}-{match.Groups[@"end"].Value} {match.Groups[@"tags"].Value}")).IsEquivalentTo([@"0:00:00.00-0:00:01.00 \an4\pos(58,1014)\fad(200,0)", @"0:00:01.00-0:00:06.00 \an4\move(58,1014,58,960,0,200)\fad(0,200)"], CollectionOrdering.Matching);
	}

	// 某一段从淡入或淡出途中开始时，接着当时的透明度变
	[Test]
	public async Task ASegmentStartingMidFadeContinuesFromTheCurrentAlphaAsync()
	{
		// 礼物 0.1 秒进来把上舰顶上去，连击 5.85 秒结束时上舰落回原位，6 秒消失
		(Match[] messages, _, _) = Generate(
		[
			new DanmakuRecord.Guard(TimeSpan.Zero, 1, @"甲", GuardLevel.Captain, 1, 198_000),
			new DanmakuRecord.Gift(TimeSpan.FromSeconds(0.1), 2, @"乙", @"礼物", 1, 1000),
			new DanmakuRecord.Gift(TimeSpan.FromSeconds(0.85), 2, @"乙", @"礼物", 1, 1000)
		]);

		await Assert.That(messages.Where(static match => match.Groups[@"text"].Value is @"甲 开通了舰长").Select(static match => $"{match.Groups[@"start"].Value}-{match.Groups[@"end"].Value} {match.Groups[@"tags"].Value}")).IsEquivalentTo(
		[
			@"0:00:00.00-0:00:00.10 \an4\pos(58,1014)\fad(200,0)",
			// 淡入过半，透明度 128，再过 100 毫秒完全显示
			@"0:00:00.10-0:00:05.85 \an4\move(58,1014,58,960,0,200)\fade(128,0,255,0,100,5700,5900)",
			// 淡出过了四分之一
			@"0:00:05.85-0:00:06.00 \an4\move(58,960,58,1014,0,200)\fade(64,64,255,0,0,0,150)"
		], CollectionOrdering.Matching);
	}

	[Test]
	public async Task NothingIsDrawnWithoutLabelsAsync()
	{
		StringWriter writer = new(CultureInfo.InvariantCulture);
		DanmakuAssResult result = DanmakuAssGenerator.Write(writer, [new DanmakuRecord.SuperChat(TimeSpan.Zero, 1, @"老板", 30_000, TimeSpan.FromSeconds(60), @"内容")], null, null);

		using (Assert.Multiple())
		{
			await Assert.That(writer.ToString()).DoesNotContain(@",Message,,");
			await Assert.That(result).IsEqualTo(new DanmakuAssResult(0, 0, 0, 0));
		}
	}

	[Test]
	public async Task ACrowdedBarDropsGiftsButShowsEverySuperChatAndGuardAsync()
	{
		List<DanmakuRecord> records = [];

		for (int i = 0; i < 10; ++i)
		{
			records.Add(new DanmakuRecord.SuperChat(TimeSpan.FromSeconds(i * 0.1), i, $@"老板{i}", 30_000, TimeSpan.FromSeconds(60), @"内容"));
			records.Add(new DanmakuRecord.Guard(TimeSpan.FromSeconds(i * 0.1), 100 + i, $@"舰长{i}", GuardLevel.Captain, 1, 198_000));
			records.Add(new DanmakuRecord.Gift(TimeSpan.FromSeconds(i * 0.1), 200 + i, $@"观众{i}", @"礼物", 1, 1000));
		}

		(Match[] messages, DanmakuAssResult result, _) = Generate(records);
		string[] texts = Texts(messages);

		using (Assert.Multiple())
		{
			await Assert.That(Enumerable.Range(0, 10).SelectMany(static i => (string[])[$@"老板{i}", $@"舰长{i} 开通了舰长"])).All(text => texts.Contains(text));
			await Assert.That(result.MessagesShown + result.MessagesDropped).IsEqualTo(30);
			await Assert.That(result.MessagesDropped).IsGreaterThan(0);
		}
	}

	[Test]
	public async Task PortraitVideoKeepsTheLandscapeSizesAndNarrowsTheBarAsync()
	{
		(Match[] messages, _, string text) = Generate(
		[
			new DanmakuRecord.SuperChat(TimeSpan.Zero, 1, @"老板", 30_000, TimeSpan.FromSeconds(60), @"内容"),
			new DanmakuRecord.Guard(TimeSpan.Zero, 2, @"一个特别特别特别特别特别特别特别长的昵称", GuardLevel.Captain, 1, 198_000)
		], new VideoResolution(1080, 1920));

		using (Assert.Multiple())
		{
			await Assert.That(text).Contains("PlayResX: 1080\nPlayResY: 1920\n");
			await Assert.That(text).Contains("Style: Danmaku,Microsoft YaHei,38,");
			await Assert.That(text).Contains("Style: Message,Microsoft YaHei,30,");
			// 宽度不超过画面宽度的 40%，即 432
			await Assert.That(text).Contains(@"}m 6 0 l 426 0 b 429 0 432 3 432 6 l 432 48 ");
			await Assert.That(Texts(messages)).Contains(@"一个特别特别特别特别… 开通了舰长");
		}
	}

	[Test]
	public async Task SmallerVideoScalesTheBarDownAsync()
	{
		(_, _, string text) = Generate([new DanmakuRecord.SuperChat(TimeSpan.Zero, 1, @"老板", 30_000, TimeSpan.FromSeconds(60), @"内容")], new VideoResolution(1280, 720));

		using (Assert.Multiple())
		{
			await Assert.That(text).Contains("Style: Message,Microsoft YaHei,20,");
			// 宽 480 × 720 / 1080 = 320，圆角半径 4
			await Assert.That(text).Contains(@"}m 4 0 l 316 0 b 318 0 320 2 320 4 l 320 32 l 0 32 l 0 4 b 0 2 2 0 4 0");
		}
	}

	[Test]
	[Arguments(30_500L, "¥30.5")]
	[Arguments(12_345L, "¥12.35")]
	[Arguments(1_999L, "¥2")]
	[Arguments(19_998_000L, "¥19998")]
	public async Task PricesAreShownInYuanAsync(long price, string expected)
	{
		await Assert.That(DanmakuAssMessages.FormatYuan(price)).IsEqualTo(expected);
	}

	public static IEnumerable<Func<(string Text, double MaxWidth, string[] Lines)>> WrapCases()
	{
		yield return static () => (new string('字', 25), 80, [new string('字', 10), new string('字', 10), new string('字', 5)]);
		// 拉丁字母在空格处换行，行首行尾的空格去掉
		yield return static () => (@"hello world  foo", 30, [@"hello", @"world", @"foo"]);
		// 混排时从最后一个全角字之后断开
		yield return static () => (@"主播 好厉害abcdeabcde", 80, [@"主播 好厉害", @"abcdeabcde"]);
		yield return static () => (string.Empty, 80, []);
	}

	// 字号 10 时全角字宽按 8 估算
	[Test]
	[MethodDataSource(nameof(WrapCases))]
	public async Task TextWrapsAfterWideCharactersAndAtSpacesAsync(string text, double maxWidth, string[] lines)
	{
		await Assert.That(DanmakuAssMessages.Wrap(text, 10, maxWidth, 5)).IsEquivalentTo(lines, CollectionOrdering.Matching);
	}

	[Test]
	public async Task TextBeyondFiveLinesEndsWithAnEllipsisAsync()
	{
		IReadOnlyList<string> lines = DanmakuAssMessages.Wrap(new string('字', 100), 10, 80, 5);

		using (Assert.Multiple())
		{
			await Assert.That(lines).Count().IsEqualTo(5);
			await Assert.That(lines).LastItem().IsEqualTo(new string('字', 9) + @"…");
			await Assert.That(lines).All(static line => DanmakuAssMessages.Measure(line, 10) <= 80);
		}
	}

	[Test]
	[Arguments("一二三", "一二三")]
	[Arguments("一二三四五", "一二三…")]
	public async Task LongNamesAreTruncatedAsync(string name, string expected)
	{
		await Assert.That(DanmakuAssMessages.Truncate(name, 10, 32)).IsEqualTo(expected);
	}

	[Test]
	public async Task ASuperChatWithoutTextIsJustTheHeaderAsync()
	{
		(Match[] messages, _, _) = Generate([new DanmakuRecord.SuperChat(TimeSpan.Zero, 1, @"老板", 30_000, TimeSpan.FromSeconds(60), string.Empty)]);
		Match shape = await Assert.That(messages).HasSingleItem(static match => match.Groups[@"layer"].Value is "1");

		// 四个角都是圆的
		await Assert.That(shape.Groups[@"text"].Value).IsEqualTo(@"m 6 0 l 474 0 b 477 0 480 3 480 6 l 480 42 b 480 45 477 48 474 48 l 6 48 b 3 48 0 45 0 42 l 0 6 b 0 3 3 0 6 0");
	}

	[GeneratedRegex(@"^Dialogue: 0,[^,]+,[^,]+,Danmaku,,0,0,0,,\{(?<tags>[^}]*)\}(?<text>.*)$")]
	private static partial Regex DanmakuRegex { get; }

	private static Dictionary<string, string> BottomPositions(string text)
	{
		return text.Split('\n').Select(static line => DanmakuRegex.Match(line)).Where(static match => match.Success).ToDictionary(static match => match.Groups[@"text"].Value, static match => match.Groups[@"tags"].Value);
	}

	// 1080p 下消息栏右边到 518，居中的弹幕估算宽度超过 884 才伸进来；30 个全角字按字号 38 估算是 1140
	private static string Wide(int i)
	{
		return $@"{i:00}" + new string('宽', 30);
	}

	private static DanmakuRecord.SuperChat BarSuperChat(double seconds = 60)
	{
		// 卡片高 104，消息栏顶端在 1036.8 - 104 = 932.8
		return new DanmakuRecord.SuperChat(TimeSpan.Zero, 1, @"老板", 30_000, TimeSpan.FromSeconds(seconds), @"内容");
	}

	[Test]
	public async Task WideBottomCommentsSkipTheLanesBesideTheBarAsync()
	{
		(_, DanmakuAssResult result, string text) = Generate([BarSuperChat(), DanmakuSamples.Comment(1, Wide(0), mode: 4), DanmakuSamples.Comment(1, Wide(1), mode: 4), DanmakuSamples.Comment(1, @"窄", mode: 4)]);
		Dictionary<string, string> positions = BottomPositions(text);

		using (Assert.Multiple())
		{
			// 最底下一条在消息栏下边距里，不重叠；往上三条和消息栏重叠，跳到第五条
			await Assert.That(positions[Wide(0)]).IsEqualTo(@"\an2\pos(960,1080)");
			await Assert.That(positions[Wide(1)]).IsEqualTo(@"\an2\pos(960,928)");
			// 窄的伸不到消息栏，照常从下往上排
			await Assert.That(positions[@"窄"]).IsEqualTo(@"\an2\pos(960,1042)");
			await Assert.That((result.Shown, result.Dropped)).IsEqualTo((3, 0));
		}
	}

	[Test]
	public async Task WhenOnlyTheLanesBesideTheBarAreFreeTheyAreStillUsedAsync()
	{
		(_, DanmakuAssResult result, string text) = Generate([BarSuperChat(), .. Enumerable.Range(0, 29).Select(static i => DanmakuSamples.Comment(1, Wide(i), mode: 4))]);
		Dictionary<string, string> positions = BottomPositions(text);

		using (Assert.Multiple())
		{
			// 1080 / 38 = 28 条轨道，和没有消息栏时丢得一样多
			await Assert.That((result.Shown, result.Dropped)).IsEqualTo((28, 1));
			await Assert.That(positions[Wide(25)]).IsEqualTo(@"\an2\pos(960,1042)");
			await Assert.That(positions[Wide(27)]).IsEqualTo(@"\an2\pos(960,966)");
		}
	}

	// 上舰从下面进来，SC 往上升 46 + 8；这一段按升上去之后的高度算，顶端在 1036.8 - 54 - 104 = 878.8
	[Test]
	public async Task BottomCommentsAvoidWhereAMovingEntryEndsUpAsync()
	{
		(_, _, string text) = Generate([BarSuperChat(), new DanmakuRecord.Guard(TimeSpan.FromSeconds(1), 2, @"舰长", GuardLevel.Captain, 1, 198_000), DanmakuSamples.Comment(1, Wide(0), mode: 4), DanmakuSamples.Comment(1, Wide(1), mode: 4)]);

		await Assert.That(BottomPositions(text)[Wide(1)]).IsEqualTo(@"\an2\pos(960,852)");
	}

	[Test]
	public async Task BottomCommentsUseTheUsualLanesWhenTheBarIsEmptyAsync()
	{
		(_, _, string text) = Generate([BarSuperChat(5), DanmakuSamples.Comment(10, Wide(0), mode: 4), DanmakuSamples.Comment(10, Wide(1), mode: 4)]);

		await Assert.That(BottomPositions(text)[Wide(1)]).IsEqualTo(@"\an2\pos(960,1042)");
	}

	// 竖屏时消息栏右边到 453.6，中线在 540，五个字的弹幕就伸进来了
	[Test]
	public async Task PortraitBottomCommentsAvoidTheBarSoonerAsync()
	{
		(_, _, string text) = Generate([BarSuperChat(), DanmakuSamples.Comment(1, @"第一条弹幕", mode: 4), DanmakuSamples.Comment(1, @"第二条弹幕", mode: 4), DanmakuSamples.Comment(1, @"第三条弹幕", mode: 4)], new VideoResolution(1080, 1920));
		Dictionary<string, string> positions = BottomPositions(text);

		using (Assert.Multiple())
		{
			// 下边距 76.8 容得下两条
			await Assert.That(positions[@"第一条弹幕"]).IsEqualTo(@"\an2\pos(540,1920)");
			await Assert.That(positions[@"第二条弹幕"]).IsEqualTo(@"\an2\pos(540,1882)");
			// 消息栏顶端在 1843.2 - 104 = 1739.2，往上第一条不重叠的底边在 1920 - 38 × 5 = 1730
			await Assert.That(positions[@"第三条弹幕"]).IsEqualTo(@"\an2\pos(540,1730)");
		}
	}

	[Test]
	public async Task TheBarHeightDuringAPeriodIsTheTallestEntryInItAsync()
	{
		DanmakuAssMessages.Occupancy occupied = new(new DanmakuAssMessages.Geometry(1920, 1080, 1), [(TimeSpan.Zero, TimeSpan.FromSeconds(10), 100), (TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), 200)]);

		using (Assert.Multiple())
		{
			await Assert.That(occupied.TopDuring(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5))).IsEqualTo(1036.8 - 100);
			await Assert.That(occupied.TopDuring(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6))).IsEqualTo(1036.8 - 200);
			await Assert.That(occupied.TopDuring(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(20))).IsEqualTo(1036.8 - 200);
			await Assert.That(occupied.TopDuring(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(20))).IsNull();
			await Assert.That(occupied.TopDuring(TimeSpan.FromSeconds(-5), TimeSpan.Zero)).IsNull();
		}
	}
}
