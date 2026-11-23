using BilibiliLive.Danmu;
using BilibiliLiveRecordDownLoader.Application.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Recording;
using System.Globalization;
using System.Text.RegularExpressions;
using TUnit.Assertions.Enums;

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

	[GeneratedRegex(@"\\c.*$")]
	private static partial Regex ColorRegex();

	private static (string Text, DanmakuAssResult Result) Generate(IEnumerable<DanmakuRecord> records, VideoResolution? size = null)
	{
		StringWriter writer = new(CultureInfo.InvariantCulture);
		DanmakuAssResult result = DanmakuAssGenerator.Write(writer, records, size, null);
		return (writer.ToString(), result);
	}

	private static Match[] Dialogues(string text)
	{
		return [.. text.Split('\n').Select(static line => DialogueRegex().Match(line)).Where(static match => match.Success)];
	}

	private static double ParseTime(string value)
	{
		return TimeSpan.ParseExact(value, @"h\:mm\:ss\.ff", CultureInfo.InvariantCulture).TotalSeconds;
	}

	private static IEnumerable<string> Texts(string text)
	{
		return Dialogues(text).Select(static match => match.Groups[@"text"].Value);
	}

	private static Dictionary<string, string> TagsByText(string text)
	{
		return Dialogues(text).ToDictionary(static match => match.Groups[@"text"].Value, static match => match.Groups[@"tags"].Value);
	}

	// 同时到达的弹幕各占一条轨道，占满后丢弃；轨道高就是弹幕样式的字号，1080 / 38 = 28 条
	// 视频宽高为 0 表示读不到画面尺寸
	[Test]
	[Arguments(0, 0, 1920, 1080, 38, 28)]
	[Arguments(1280, 720, 1280, 720, 25, 28)]
	// 竖屏按短边缩放，字不跟着高度放大，轨道就多了
	[Arguments(1080, 1920, 1080, 1920, 38, 50)]
	public async Task LanesFollowTheShortSideAndDefaultTo1080pAsync(int videoWidth, int videoHeight, int width, int height, int fontSize, int lanes)
	{
		VideoResolution? size = videoWidth > 0 ? new VideoResolution(videoWidth, videoHeight) : null;
		(string text, DanmakuAssResult result) = Generate([.. Enumerable.Range(0, 60).Select(static i => DanmakuSamples.Comment(10, $@"弹幕{i}"))], size);

		using (Assert.Multiple())
		{
			await Assert.That(text).Contains(string.Create(CultureInfo.InvariantCulture, $"PlayResX: {width}\nPlayResY: {height}\n"));
			await Assert.That(text).Contains(string.Create(CultureInfo.InvariantCulture, $"Style: Danmaku,Microsoft YaHei,{fontSize},"));
			await Assert.That(result).IsEqualTo(new DanmakuAssResult(lanes, 60 - lanes, 0, 0));
			await Assert.That(Dialogues(text).Select(static match => int.Parse(MoveRegex().Match(match.Groups[@"tags"].Value).Groups[@"y1"].Value, CultureInfo.InvariantCulture))).IsEquivalentTo(Enumerable.Range(0, lanes).Select(lane => lane * fontSize));
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
		Placed[] placed = [.. Dialogues(ass).Select(static match =>
		{
			string tags = match.Groups[@"tags"].Value;
			Match move = MoveRegex().Match(tags);
			int fontSize = FontSizeRegex().Match(tags) is { Success: true } fs ? int.Parse(fs.Groups[@"size"].Value, CultureInfo.InvariantCulture) : 38;
			return new Placed(ParseTime(match.Groups[@"start"].Value), int.Parse(move.Groups[@"y1"].Value, CultureInfo.InvariantCulture), fontSize, -int.Parse(move.Groups[@"x2"].Value, CultureInfo.InvariantCulture));
		})];
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
		Dictionary<string, string> tags = TagsByText(text);

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
		Dictionary<string, string> tags = TagsByText(text);

		using (Assert.Multiple())
		{
			await Assert.That(tags[@"大"]).Contains(@"\fs36");
			await Assert.That(tags[@"小"]).Contains(@"\fs18");
			await Assert.That(tags[@"中"]).DoesNotContain(@"\fs");
		}
	}

	// 颜色是最后一个标签；白色是样式里的颜色，不用写
	[Test]
	[Arguments(0xFF0000, @"\c&H0000FF&")]
	[Arguments(0x123456, @"\c&H563412&")]
	[Arguments(0xFFFFFF, "")]
	public async Task ColorsAreConvertedToBgrAndWhiteNeedsNoTagAsync(int rgb, string expected)
	{
		(string text, _) = Generate([DanmakuSamples.Comment(0) with { Color = rgb }]);
		Match dialogue = await Assert.That(Dialogues(text)).HasSingleItem();

		await Assert.That(ColorRegex().Match(dialogue.Groups[@"tags"].Value).Value).IsEqualTo(expected);
	}

	[Test]
	public async Task OverrideCharactersAreShownLiterallyAsync()
	{
		(string text, _) = Generate([DanmakuSamples.Comment(0, "{\\b1}换\\N行\n第二行\u0007")]);

		await Assert.That(Texts(text)).IsEquivalentTo([@"｛＼b1｝换＼N行 第二行"]);
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
			new DanmakuRecord.Guard(TimeSpan.Zero, 1, @"舰长", GuardLevel.Captain, 1, 198000),
			DanmakuSamples.Comment(0, @"滚动", mode: 2)
		];

		(string text, DanmakuAssResult result) = Generate(records);

		using (Assert.Multiple())
		{
			await Assert.That(result).IsEqualTo(new DanmakuAssResult(1, 0, 0, 0));
			await Assert.That(Texts(text)).IsEquivalentTo([@"滚动"]);
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

		await Assert.That(Texts(text)).IsEquivalentTo([@"前", @"后"], CollectionOrdering.Matching);
	}

	[Test]
	public async Task FileStartsWithAByteOrderMarkAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine(@"弹幕.ass");

		await DanmakuAssGenerator.WriteFileAsync(path, [DanmakuSamples.Comment(0)], null, null, cancellationToken);
		byte[] content = await File.ReadAllBytesAsync(path, cancellationToken);

		await Assert.That(content.Take(3)).IsEquivalentTo((byte[])[0xEF, 0xBB, 0xBF], CollectionOrdering.Matching);
	}

	private readonly record struct Placed(double Start, int Top, int Height, int Width);
}
