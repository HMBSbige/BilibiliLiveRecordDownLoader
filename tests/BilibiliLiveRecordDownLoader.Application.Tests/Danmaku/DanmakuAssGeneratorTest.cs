using BilibiliLiveRecordDownLoader.Application.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Recording;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Danmaku;

public sealed partial class DanmakuAssGeneratorTest
{
	private const double ScrollSeconds = 12;
	private const double Tolerance = 0.02;

	[GeneratedRegex(@"^Dialogue: 0,(?<start>[^,]+),(?<end>[^,]+),Danmaku,,0,0,0,,\{(?<tags>[^}]*)\}(?<text>.*)$")]
	private static partial Regex DialogueRegex();

	[GeneratedRegex(@"\\move\((?<x1>-?\d+),(?<y1>-?\d+),(?<x2>-?\d+),(?<y2>-?\d+)\)")]
	private static partial Regex MoveRegex();

	[GeneratedRegex(@"\\fs(?<size>\d+)")]
	private static partial Regex FontSizeRegex();

	private static (string Text, DanmakuAssResult Result) Generate(IEnumerable<DanmakuRecord> records, VideoResolution? size = null)
	{
		StringWriter writer = new(CultureInfo.InvariantCulture);
		DanmakuAssResult result = DanmakuAssGenerator.Write(writer, records, size, null);
		return (writer.ToString(), result);
	}

	private static Match[] Dialogues(string text)
	{
		return text.Split('\n').Select(static line => DialogueRegex().Match(line)).Where(static match => match.Success).ToArray();
	}

	private static double ParseTime(string value)
	{
		return TimeSpan.ParseExact(value, @"h\:mm\:ss\.ff", CultureInfo.InvariantCulture).TotalSeconds;
	}

	[Test]
	public async Task DefaultsTo1080pWhenTheResolutionIsUnknownAsync()
	{
		(string text, _) = Generate([DanmakuSamples.Comment(1)]);
		(string scaled, _) = Generate([DanmakuSamples.Comment(1)], new VideoResolution(1280, 720));

		using (Assert.Multiple())
		{
			await Assert.That(text).Contains("PlayResX: 1920\nPlayResY: 1080\n");
			await Assert.That(text).Contains("Style: Danmaku,Microsoft YaHei,38,");
			await Assert.That(scaled).Contains("PlayResX: 1280\nPlayResY: 720\n");
			await Assert.That(scaled).Contains("Style: Danmaku,Microsoft YaHei,25,");
		}
	}

	[Test]
	public async Task PortraitVideoScalesByTheShortSideAsync()
	{
		DanmakuRecord[] records = Enumerable.Range(0, 60).Select(static i => (DanmakuRecord)DanmakuSamples.Comment(10, $@"弹幕{i}")).ToArray();

		(string text, DanmakuAssResult result) = Generate(records, new VideoResolution(1080, 1920));

		using (Assert.Multiple())
		{
			await Assert.That(text).Contains("PlayResX: 1080\nPlayResY: 1920\n");
			await Assert.That(text).Contains("Style: Danmaku,Microsoft YaHei,38,");
			// 1920 / 38 = 50 条轨道
			await Assert.That(result).IsEqualTo(new DanmakuAssResult(50, 10, 0, 0));
		}
	}

	[Test]
	public async Task SimultaneousCommentsTakeSeparateLanesAndTheRestAreDroppedAsync()
	{
		DanmakuRecord[] records = Enumerable.Range(0, 40).Select(static i => (DanmakuRecord)DanmakuSamples.Comment(10, $@"弹幕{i}")).ToArray();

		(string text, DanmakuAssResult result) = Generate(records);
		int[] lanes = Dialogues(text).Select(static match => int.Parse(MoveRegex().Match(match.Groups[@"tags"].Value).Groups[@"y1"].Value, CultureInfo.InvariantCulture)).ToArray();

		using (Assert.Multiple())
		{
			// 1080 / 38 = 28 条轨道
			await Assert.That(result).IsEqualTo(new DanmakuAssResult(28, 12, 0, 0));
			await Assert.That(lanes.Distinct().Count()).IsEqualTo(28);
			await Assert.That(lanes.Max()).IsEqualTo(27 * 38);
		}
	}

	// 同一轨道上前一条的尾部进入屏幕之前后一条不出现，前一条离开屏幕之前后一条追不上
	[Test]
	public async Task ScrollingCommentsInTheSameLaneNeverOverlapAsync()
	{
		List<DanmakuRecord> records = [];
		Random random = new(106);

		for (int i = 0; i < 3000; ++i)
		{
			string text = new('字', random.Next(1, 30));
			records.Add(DanmakuSamples.Comment(i * 0.1 + random.NextDouble() * 0.05, text) with { FontSize = random.Next(3) switch { 0 => 18, 1 => 25, _ => 36 } });
		}

		(string ass, DanmakuAssResult result) = Generate(records);
		Placed[] placed = Dialogues(ass).Select(static match =>
		{
			string tags = match.Groups[@"tags"].Value;
			Match move = MoveRegex().Match(tags);
			int fontSize = FontSizeRegex().Match(tags) is { Success: true } fs ? int.Parse(fs.Groups[@"size"].Value, CultureInfo.InvariantCulture) : 38;
			return new Placed(ParseTime(match.Groups[@"start"].Value), int.Parse(move.Groups[@"y1"].Value, CultureInfo.InvariantCulture), fontSize, -int.Parse(move.Groups[@"x2"].Value, CultureInfo.InvariantCulture));
		}).ToArray();
		int violations = 0;

		for (int i = 0; i < placed.Length; ++i)
		{
			for (int j = i + 1; j < placed.Length && placed[j].Start - placed[i].Start < ScrollSeconds; ++j)
			{
				(Placed earlier, Placed later) = (placed[i], placed[j]);

				if (later.Top >= earlier.Top + earlier.Height || earlier.Top >= later.Top + later.Height)
				{
					continue;
				}

				double elapsed = later.Start - earlier.Start;
				double earlierSpeed = (1920 + earlier.Width) / ScrollSeconds;
				double laterSpeed = (1920 + later.Width) / ScrollSeconds;
				// ASS 的时间精度是百分之一秒，宽度也取过整，留出一点余量
				bool tailEntered = elapsed * earlierSpeed >= earlier.Width - earlierSpeed * Tolerance;
				bool neverCatchesUp = (ScrollSeconds - elapsed) * laterSpeed <= 1920 + laterSpeed * Tolerance;

				if (!tailEntered || !neverCatchesUp)
				{
					++violations;
				}
			}
		}

		using (Assert.Multiple())
		{
			await Assert.That(violations).IsEqualTo(0);
			await Assert.That(result.Shown).IsGreaterThan(1000);
			await Assert.That(result.Dropped).IsGreaterThan(0);
			await Assert.That(result.Shown + result.Dropped).IsEqualTo(records.Count);
		}
	}

	[Test]
	public async Task FixedCommentsStackFromTheirEdgeAndFreeTheLaneAfterFiveSecondsAsync()
	{
		DanmakuRecord[] records =
		[
			DanmakuSamples.Comment(0, @"顶1", mode: 5),
			DanmakuSamples.Comment(1, @"顶2", mode: 5),
			DanmakuSamples.Comment(5, @"顶3", mode: 5),
			DanmakuSamples.Comment(0, @"底1", mode: 4),
			DanmakuSamples.Comment(1, @"底2", mode: 4)
		];

		(string text, _) = Generate(records);
		Dictionary<string, string> tags = Dialogues(text).ToDictionary(static match => match.Groups[@"text"].Value, static match => match.Groups[@"tags"].Value);

		using (Assert.Multiple())
		{
			await Assert.That(tags[@"顶1"]).IsEqualTo(@"\an8\pos(960,0)");
			await Assert.That(tags[@"顶2"]).IsEqualTo(@"\an8\pos(960,38)");
			await Assert.That(tags[@"顶3"]).IsEqualTo(@"\an8\pos(960,0)");
			await Assert.That(tags[@"底1"]).IsEqualTo(@"\an2\pos(960,1080)");
			await Assert.That(tags[@"底2"]).IsEqualTo(@"\an2\pos(960,1042)");
			await Assert.That(text).Contains(@"Dialogue: 0,0:00:01.00,0:00:06.00,Danmaku,,0,0,0,,{\an8\pos(960,38)}顶2");
		}
	}

	[Test]
	public async Task FontSizeScalesWithResolutionAndDanmakuSizeAsync()
	{
		(string text, _) = Generate([DanmakuSamples.Comment(0, @"大") with { FontSize = 36 }, DanmakuSamples.Comment(0, @"小") with { FontSize = 18 }, DanmakuSamples.Comment(0, @"中")], new VideoResolution(1280, 720));
		Dictionary<string, string> tags = Dialogues(text).ToDictionary(static match => match.Groups[@"text"].Value, static match => match.Groups[@"tags"].Value);

		using (Assert.Multiple())
		{
			await Assert.That(tags[@"大"]).Contains(@"\fs36");
			await Assert.That(tags[@"小"]).Contains(@"\fs18");
			await Assert.That(tags[@"中"].Contains(@"\fs", StringComparison.Ordinal)).IsFalse();
		}
	}

	[Test]
	[Arguments(0xFF0000, "0000FF")]
	[Arguments(0x00FF00, "00FF00")]
	[Arguments(0x123456, "563412")]
	public async Task ColorsAreConvertedToBgrAsync(int rgb, string expected)
	{
		(string text, _) = Generate([DanmakuSamples.Comment(0) with { Color = rgb }]);

		await Assert.That(Dialogues(text).Single().Groups[@"tags"].Value).EndsWith($@"\c&H{expected}&");
	}

	[Test]
	public async Task WhiteNeedsNoColorTagAsync()
	{
		(string text, _) = Generate([DanmakuSamples.Comment(0)]);

		await Assert.That(Dialogues(text).Single().Groups[@"tags"].Value.Contains(@"\c", StringComparison.Ordinal)).IsFalse();
	}

	[Test]
	public async Task OverrideCharactersAreShownLiterallyAsync()
	{
		(string text, _) = Generate([DanmakuSamples.Comment(0, "{\\b1}换\\N行\n第二行\u0007")]);

		await Assert.That(Dialogues(text).Single().Groups[@"text"].Value).IsEqualTo(@"｛＼b1｝换＼N行 第二行");
	}

	[Test]
	public async Task OnlyPlainCommentsInSupportedModesAreShownAsync()
	{
		DanmakuRecord[] records =
		[
			DanmakuSamples.Comment(0, @"逆向", mode: 6),
			DanmakuSamples.Comment(0, @"高级", mode: 7),
			DanmakuSamples.Comment(0, @"   "),
			new DanmakuRecord.SuperChat(TimeSpan.Zero, 1, @"老板", 30000, TimeSpan.FromSeconds(60), @"醒目留言"),
			new DanmakuRecord.Gift(TimeSpan.Zero, 1, @"观众", @"小花花", 1, 100),
			new DanmakuRecord.Guard(TimeSpan.Zero, 1, @"舰长", 3, 1, 198000),
			DanmakuSamples.Comment(0, @"滚动", mode: 2)
		];

		(string text, DanmakuAssResult result) = Generate(records);

		using (Assert.Multiple())
		{
			await Assert.That(result).IsEqualTo(new DanmakuAssResult(1, 0, 0, 0));
			await Assert.That(Dialogues(text).Single().Groups[@"text"].Value).IsEqualTo(@"滚动");
		}
	}

	[Test]
	public async Task TimesUseCentisecondsAndHoursAsync()
	{
		(string text, _) = Generate([DanmakuSamples.Comment(3661.239)]);

		await Assert.That(text).Contains(@"Dialogue: 0,1:01:01.23,1:01:13.23,");
	}

	[Test]
	public async Task OutputIsSortedByTimeAsync()
	{
		(string text, _) = Generate([DanmakuSamples.Comment(5, @"后"), DanmakuSamples.Comment(1, @"前")]);

		await Assert.That(Dialogues(text).Select(static match => match.Groups[@"text"].Value)).IsEquivalentTo([@"前", @"后"], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task FileStartsWithAByteOrderMarkAsync()
	{
		using TestDirectory directory = new();
		string path = directory.Combine(@"弹幕.ass");

		await DanmakuAssGenerator.WriteFileAsync(path, [DanmakuSamples.Comment(0)], null, null, CancellationToken.None);
		byte[] content = await File.ReadAllBytesAsync(path);

		await Assert.That(content is [0xEF, 0xBB, 0xBF, ..]).IsTrue();
	}

	private readonly record struct Placed(double Start, int Top, int Height, int Width);
}
