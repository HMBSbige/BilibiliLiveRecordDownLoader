using BilibiliLiveRecordDownLoader.Application.Danmaku;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TUnit.Assertions.Enums;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Danmaku;

public sealed partial class DanmakuXmlWriterTest
{
	[GeneratedRegex(@"<d p=""(?<p>[^""]*)""")]
	private static partial Regex CommentFieldsRegex();

	[Test]
	public async Task NewFileIsCompleteXmlBeforeAnyRecordAsync()
	{
		MemoryStream stream = new();

		await using DanmakuXmlWriter writer = await DanmakuXmlWriter.CreateAsync(stream, DanmakuSamples.Header, DanmakuFlushPolicy.Default, TimeProvider.System, CancellationToken.None);
		Dictionary<string, int> elements = DanmakuSamples.ParseStrictly(stream.ToArray());
		string text = Encoding.UTF8.GetString(stream.ToArray());

		using (Assert.Multiple())
		{
			await Assert.That(elements.GetValueOrDefault(@"BilibiliLiveRecordDownLoader")).IsEqualTo(1);
			await Assert.That(elements.GetValueOrDefault(@"chatserver")).IsEqualTo(1);
			await Assert.That(text.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<i>\n", StringComparison.Ordinal)).IsTrue();
			await Assert.That(text.EndsWith("</i>\n", StringComparison.Ordinal)).IsTrue();
		}
	}

	[Test]
	public async Task EveryFlushLeavesACompleteDocumentAsync()
	{
		MemoryStream stream = new();
		DanmakuFlushPolicy policy = new(TimeSpan.FromHours(1), 3);
		DanmakuRecord[] records = [.. DanmakuSamples.AllKinds(), .. Enumerable.Range(0, 20).Select(static i => DanmakuSamples.Comment(200 + i, $@"第{i}条"))];
		List<long> flushedCounts = [];

		await using (DanmakuXmlWriter writer = await DanmakuXmlWriter.CreateAsync(stream, DanmakuSamples.Header, policy, TimeProvider.System, CancellationToken.None))
		{
			foreach (DanmakuRecord record in records)
			{
				await writer.WriteAsync(record, CancellationToken.None);

				// 每次写入后文件都必须能被严格的解析器完整读完，读到的记录数等于已刷新的条数
				Dictionary<string, int> elements = DanmakuSamples.ParseStrictly(stream.ToArray());
				int recordElements = elements.Where(static pair => pair.Key is @"d" or @"sc" or @"sc_delete" or @"gift" or @"guard" or @"title" or @"gap" or @"connection").Sum(static pair => pair.Value);
				await Assert.That((long)recordElements).IsEqualTo(writer.WrittenCount);
				flushedCounts.Add(writer.WrittenCount);
			}
		}

		await Assert.That(flushedCounts.Distinct().Count()).IsGreaterThan(5);
	}

	[Test]
	public async Task DisposingFlushesPendingRecordsAsync()
	{
		byte[] content = await DanmakuSamples.WriteAsync([DanmakuSamples.Comment(1), DanmakuSamples.Comment(2)]);

		await Assert.That(DanmakuSamples.ParseStrictly(content).GetValueOrDefault(@"d")).IsEqualTo(2);
	}

	[Test]
	public async Task FlushesByCountAsync()
	{
		MemoryStream stream = new();
		FakeTimeProvider time = new();

		await using DanmakuXmlWriter writer = await DanmakuXmlWriter.CreateAsync(stream, DanmakuSamples.Header, new DanmakuFlushPolicy(TimeSpan.FromSeconds(5), 3), time, CancellationToken.None);
		await writer.WriteAsync(DanmakuSamples.Comment(1), CancellationToken.None);
		await writer.WriteAsync(DanmakuSamples.Comment(2), CancellationToken.None);
		long beforeThird = writer.WrittenCount;
		await writer.WriteAsync(DanmakuSamples.Comment(3), CancellationToken.None);

		using (Assert.Multiple())
		{
			await Assert.That(beforeThird).IsEqualTo(0);
			await Assert.That(writer.WrittenCount).IsEqualTo(3);
			await Assert.That(writer.PendingCount).IsEqualTo(0);
			await Assert.That(writer.FlushDueIn).IsNull();
		}
	}

	[Test]
	public async Task FlushesByTimeEvenWhenNothingElseArrivesAsync()
	{
		MemoryStream stream = new();
		FakeTimeProvider time = new();

		await using DanmakuXmlWriter writer = await DanmakuXmlWriter.CreateAsync(stream, DanmakuSamples.Header, new DanmakuFlushPolicy(TimeSpan.FromSeconds(5), 100), time, CancellationToken.None);
		await writer.WriteAsync(DanmakuSamples.Comment(1), CancellationToken.None);
		TimeSpan? dueAfterWrite = writer.FlushDueIn;
		time.Advance(TimeSpan.FromSeconds(3));
		await writer.FlushIfDueAsync(CancellationToken.None);
		(long writtenEarly, TimeSpan? dueEarly) = (writer.WrittenCount, writer.FlushDueIn);
		time.Advance(TimeSpan.FromSeconds(2));
		await writer.FlushIfDueAsync(CancellationToken.None);

		using (Assert.Multiple())
		{
			await Assert.That(dueAfterWrite).IsEqualTo(TimeSpan.FromSeconds(5));
			await Assert.That(writtenEarly).IsEqualTo(0);
			await Assert.That(dueEarly).IsEqualTo(TimeSpan.FromSeconds(2));
			await Assert.That(writer.WrittenCount).IsEqualTo(1);
			await Assert.That(DanmakuSamples.ParseStrictly(stream.ToArray()).GetValueOrDefault(@"d")).IsEqualTo(1);
		}
	}

	[Test]
	public async Task WriteFlushesWhenTheOldestPendingRecordIsDueAsync()
	{
		MemoryStream stream = new();
		FakeTimeProvider time = new();

		await using DanmakuXmlWriter writer = await DanmakuXmlWriter.CreateAsync(stream, DanmakuSamples.Header, new DanmakuFlushPolicy(TimeSpan.FromSeconds(5), 100), time, CancellationToken.None);
		await writer.WriteAsync(DanmakuSamples.Comment(1), CancellationToken.None);
		time.Advance(TimeSpan.FromSeconds(5));
		await writer.WriteAsync(DanmakuSamples.Comment(2), CancellationToken.None);

		await Assert.That(writer.WrittenCount).IsEqualTo(2);
	}

	[Test]
	public async Task CommentFieldsFollowTheBilibiliLayoutAsync()
	{
		byte[] content = await DanmakuSamples.WriteAsync(
		[
			new DanmakuRecord.Comment(TimeSpan.FromMilliseconds(12345), 1, 25, 16777215, DanmakuSamples.SentAt, 123, @"昵称", @"文本", @"5e7c63ca70d385b033f842f35a6b02c84761"),
			new DanmakuRecord.Comment(TimeSpan.FromMilliseconds(500), 5, 36, 0xFF0000, DanmakuSamples.SentAt.AddMilliseconds(999), 0, @"淡***", @"匿名", string.Empty),
			new DanmakuRecord.Comment(TimeSpan.FromMilliseconds(600), 1, 25, 16777215, DanmakuSamples.SentAt, 0, @"淡***", @"打码", @"0") { UidHash = @"a16fe0dd" }
		]);
		string[] lines = DanmakuSamples.Lines(content).Where(static line => line.StartsWith(@"  <d ", StringComparison.Ordinal)).ToArray();
		string[] first = CommentFieldsRegex().Match(lines[0]).Groups[@"p"].Value.Split(',');
		string[] second = CommentFieldsRegex().Match(lines[1]).Groups[@"p"].Value.Split(',');

		using (Assert.Multiple())
		{
			await Assert.That(lines[0]).IsEqualTo("  <d p=\"12.345,1,25,16777215,1795343190,0,884863d2,5e7c63ca70d385b033f842f35a6b02c84761\" uid=\"123\" user=\"昵称\">文本</d>");
			await Assert.That(first[0]).IsEqualTo(@"12.345");
			await Assert.That(first[1]).IsEqualTo(@"1");
			await Assert.That(first[2]).IsEqualTo(@"25");
			await Assert.That(first[3]).IsEqualTo(@"16777215");
			await Assert.That(first[4]).IsEqualTo(@"1795343190");
			await Assert.That(first[5]).IsEqualTo(@"0");
			await Assert.That(first[6]).IsEqualTo(@"884863d2");
			await Assert.That(first[7]).IsEqualTo(@"5e7c63ca70d385b033f842f35a6b02c84761");
			await Assert.That(first.Length).IsEqualTo(8);
			// 毫秒部分截掉，uid 为 0 时哈希写 0，没有 dmid 写 0
			await Assert.That(second).IsEquivalentTo([@"0.500", @"5", @"36", @"16711680", @"1795343190", @"0", @"0", @"0"], StringComparer.Ordinal, CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task PricesAreWrittenInThousandthsOfAYuanAsync()
	{
		byte[] content = await DanmakuSamples.WriteAsync(DanmakuSamples.AllKinds());
		string[] lines = DanmakuSamples.Lines(content);

		using (Assert.Multiple())
		{
			await Assert.That(lines).Contains("  <sc ts=\"20.000\" uid=\"456\" user=\"老板\" price=\"30000\" time=\"60\" id=\"20541912\">醒目留言</sc>");
			await Assert.That(lines).Contains("  <sc_delete ts=\"110.000\" ids=\"20541912,20541913\" />");
			await Assert.That(lines).Contains("  <gift ts=\"21.500\" uid=\"789\" user=\"观众\" giftname=\"小花花\" giftcount=\"10\" price=\"1000\" />");
			await Assert.That(lines).Contains("  <guard ts=\"30.000\" uid=\"1011\" user=\"舰长\" level=\"3\" count=\"1\" price=\"198000\" />");
			await Assert.That(lines).Contains("  <guard ts=\"120.000\" uid=\"1012\" user=\"老舰长\" level=\"2\" count=\"3\" price=\"4194000\" renewal=\"1\" />");
			await Assert.That(lines).Contains("  <title ts=\"40.000\">新标题</title>");
			await Assert.That(lines).Contains("  <gap start=\"100.000\" end=\"105.200\" />");
			await Assert.That(lines).Contains("  <connection ts=\"0.000\" anonymous=\"true\" masked=\"true\" />");
			await Assert.That(lines).Contains("  <d p=\"13.000,5,36,16711680,1795343190,0,a16fe0dd,0\" uid=\"0\" user=\"淡***\" storm=\"1\">节奏风暴</d>");
		}
	}

	[Test]
	public async Task HeaderCarriesMetadataWithIsoTimestampsAsync()
	{
		byte[] content = await DanmakuSamples.WriteAsync([], DanmakuSamples.Header with { Offset = TimeSpan.FromMilliseconds(-1500), LiveStartTime = null });
		string line = DanmakuSamples.Lines(content).Single(static line => line.Contains(@"<BilibiliLiveRecordDownLoader", StringComparison.Ordinal));

		await Assert.That(line).IsEqualTo("  <BilibiliLiveRecordDownLoader version=\"1.2.3\" room_id=\"21452505\" short_id=\"0\" streamer=\"测试主播\" title=\"测试标题\" area=\"虚拟主播\" parent_area=\"虚拟主播\" live_id=\"745899535714532510\" live_start_time=\"\" record_start_time=\"2026-11-22T18:26:00.123+08:00\" offset=\"-1.500\" />");
	}

	[Test]
	public async Task OffsetIsAddedAndNegativeTimesBecomeZeroAsync()
	{
		byte[] later = await DanmakuSamples.WriteAsync([DanmakuSamples.Comment(1), new DanmakuRecord.Gap(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2))], DanmakuSamples.Header with { Offset = TimeSpan.FromMilliseconds(2500) });
		byte[] earlier = await DanmakuSamples.WriteAsync([DanmakuSamples.Comment(1), DanmakuSamples.Comment(10)], DanmakuSamples.Header with { Offset = TimeSpan.FromSeconds(-5) });

		using (Assert.Multiple())
		{
			await Assert.That(DanmakuSamples.Lines(later)).Contains(static line => line.StartsWith("  <d p=\"3.500,", StringComparison.Ordinal));
			await Assert.That(DanmakuSamples.Lines(later)).Contains("  <gap start=\"3.500\" end=\"4.500\" />");
			await Assert.That(DanmakuSamples.Lines(earlier)).Contains(static line => line.StartsWith("  <d p=\"0.000,", StringComparison.Ordinal));
			await Assert.That(DanmakuSamples.Lines(earlier)).Contains(static line => line.StartsWith("  <d p=\"5.000,", StringComparison.Ordinal));
		}
	}

	[Test]
	public async Task InvalidCharactersAreDroppedAndEachElementStaysOnOneLineAsync()
	{
		string text = "a\u0001b\nc\uD800d\uFFFE<&>\"\U0001F600";
		byte[] content = await DanmakuSamples.WriteAsync([DanmakuSamples.Comment(1, text), new DanmakuRecord.TitleChange(TimeSpan.Zero, "第一行\r\n第二行")]);
		DanmakuFile file = await DanmakuSamples.ReadAsync(content);

		using (Assert.Multiple())
		{
			await Assert.That(DanmakuSamples.ParseStrictly(content).GetValueOrDefault(@"d")).IsEqualTo(1);
			await Assert.That(DanmakuSamples.Lines(content).Length).IsEqualTo(7);
			await Assert.That(file.Records.OfType<DanmakuRecord.Comment>().Single().Text).IsEqualTo("ab\ncd<&>\"\U0001F600");
			await Assert.That(file.Records.OfType<DanmakuRecord.TitleChange>().Single().Title).IsEqualTo("第一行\r\n第二行");
		}
	}

	[Test]
	public async Task NumbersDoNotDependOnTheCurrentCultureAsync()
	{
		CultureInfo original = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(@"de-DE");

		try
		{
			byte[] content = await DanmakuSamples.WriteAsync([DanmakuSamples.Comment(12.345)], DanmakuSamples.Header with { Offset = TimeSpan.FromMilliseconds(250) });
			DanmakuFile file = await DanmakuSamples.ReadAsync(content);

			using (Assert.Multiple())
			{
				await Assert.That(DanmakuSamples.Lines(content)).Contains(static line => line.StartsWith("  <d p=\"12.595,", StringComparison.Ordinal));
				await Assert.That(DanmakuSamples.Lines(content)).Contains(static line => line.Contains("offset=\"0.250\"", StringComparison.Ordinal));
				await Assert.That(file.Records.Single().Time).IsEqualTo(TimeSpan.FromMilliseconds(12595));
			}
		}
		finally
		{
			CultureInfo.CurrentCulture = original;
		}
	}

	[Test]
	public async Task FileCanBeReadWhileRecordingAsync()
	{
		using TestDirectory directory = new();
		string path = directory.Combine(@"录制.xml");

		await using DanmakuXmlWriter writer = await DanmakuXmlWriter.CreateFileAsync(path, DanmakuSamples.Header, new DanmakuFlushPolicy(TimeSpan.FromSeconds(5), 1), TimeProvider.System, CancellationToken.None);
		await writer.WriteAsync(DanmakuSamples.Comment(1), CancellationToken.None);
		DanmakuFile file = await DanmakuXmlReader.ReadFileAsync(path, CancellationToken.None);

		using (Assert.Multiple())
		{
			await Assert.That(file.IsComplete).IsTrue();
			await Assert.That(file.Records.Count).IsEqualTo(1);
		}
	}

	[Test]
	public async Task CreatingAFileNeverOverwritesAsync()
	{
		using TestDirectory directory = new();
		string path = directory.Combine(@"已存在.xml");
		await File.WriteAllTextAsync(path, @"keep");

		await Assert.That(async () => await DanmakuXmlWriter.CreateFileAsync(path, DanmakuSamples.Header, DanmakuFlushPolicy.Default, TimeProvider.System, CancellationToken.None)).Throws<IOException>();
		await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(@"keep");
	}
}
