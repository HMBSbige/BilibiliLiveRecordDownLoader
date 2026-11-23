using BilibiliLiveRecordDownLoader.Application.Danmaku;
using System.Globalization;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Danmaku;

public sealed class DanmakuXmlWriterTest
{
	[Test]
	public async Task NewFileIsCompleteXmlWithTheHeaderBeforeAnyRecordAsync(CancellationToken cancellationToken)
	{
		MemoryStream stream = new();

		await using DanmakuXmlWriter writer = await DanmakuXmlWriter.CreateAsync(stream, DanmakuSamples.Header with { Offset = TimeSpan.FromMilliseconds(-1500), LiveStartTime = null }, DanmakuFlushPolicy.Default, TimeProvider.System, cancellationToken);

		await Assert.That(Encoding.UTF8.GetString(stream.ToArray()).Split('\n')).IsEquivalentTo(
		[
			"<?xml version=\"1.0\" encoding=\"utf-8\"?>",
			"<i>",
			"  <chatserver>chat.bilibili.com</chatserver><chatid>0</chatid><mission>0</mission><maxlimit>0</maxlimit><state>0</state><real_name>0</real_name><source>k-v</source>",
			"  <BilibiliLiveRecordDownLoader version=\"1.2.3\" room_id=\"21452505\" short_id=\"0\" streamer=\"测试主播\" title=\"测试标题\" area=\"虚拟主播\" parent_area=\"虚拟主播\" live_id=\"745899535714532510\" live_start_time=\"\" record_start_time=\"2026-11-22T18:26:00.123+08:00\" offset=\"-1.500\" />",
			"</i>",
			""
		], CollectionOrdering.Matching);
	}

	[Test]
	public async Task EveryFlushLeavesACompleteDocumentAsync(CancellationToken cancellationToken)
	{
		MemoryStream stream = new();
		DanmakuFlushPolicy policy = new(TimeSpan.FromHours(1), 3);
		DanmakuRecord[] records = [.. DanmakuSamples.AllKinds(), .. Enumerable.Range(0, 20).Select(static i => DanmakuSamples.Comment(200 + i, $@"第{i}条"))];
		List<long> flushedCounts = [];

		await using (DanmakuXmlWriter writer = await DanmakuXmlWriter.CreateAsync(stream, DanmakuSamples.Header, policy, TimeProvider.System, cancellationToken))
		{
			foreach (DanmakuRecord record in records)
			{
				await writer.WriteAsync(record, cancellationToken);

				Dictionary<string, int> elements = DanmakuSamples.ParseStrictly(stream.ToArray());
				int recordElements = elements.Where(static pair => pair.Key is @"d" or @"sc" or @"sc_delete" or @"gift" or @"guard" or @"title" or @"gap" or @"connection").Sum(static pair => pair.Value);
				await Assert.That((long)recordElements).IsEqualTo(writer.WrittenCount);
				flushedCounts.Add(writer.WrittenCount);
			}
		}

		await Assert.That(flushedCounts.Distinct()).Count().IsGreaterThan(5);
	}

	[Test]
	public async Task FailedFlushIsRewrittenFromTheSamePositionAsync(CancellationToken cancellationToken)
	{
		MemoryStream inner = new();
		FailingStream stream = new(inner);
		DanmakuFlushPolicy policy = new(TimeSpan.FromHours(1), 1);
		DanmakuRecord.Comment first = DanmakuSamples.Comment(1, @"第一条");
		DanmakuRecord.Comment second = DanmakuSamples.Comment(2, @"第二条");

		await using (DanmakuXmlWriter writer = await DanmakuXmlWriter.CreateAsync(stream, DanmakuSamples.Header, policy, TimeProvider.System, cancellationToken))
		{
			stream.FailNextWrite = true;
			await Assert.That(async () => await writer.WriteAsync(first, cancellationToken)).Throws<IOException>();
			await Assert.That(writer.PendingCount).IsEqualTo(1);
			await writer.WriteAsync(second, cancellationToken);
		}

		byte[] content = inner.ToArray();
		DanmakuFile file = await DanmakuSamples.ReadAsync(content, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(DanmakuSamples.ParseStrictly(content).GetValueOrDefault(@"d")).IsEqualTo(2);
			await Assert.That(file.IsComplete).IsTrue();
			await Assert.That(file.Records).IsEquivalentTo([first, second], EqualityComparer<DanmakuRecord>.Default, CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task FlushesByCountAsync(CancellationToken cancellationToken)
	{
		FakeTimeProvider time = new();

		await using DanmakuXmlWriter writer = await DanmakuXmlWriter.CreateAsync(new MemoryStream(), DanmakuSamples.Header, new DanmakuFlushPolicy(TimeSpan.FromSeconds(5), 3), time, cancellationToken);
		await writer.WriteAsync(DanmakuSamples.Comment(1), cancellationToken);
		await writer.WriteAsync(DanmakuSamples.Comment(2), cancellationToken);
		long beforeThird = writer.WrittenCount;
		await writer.WriteAsync(DanmakuSamples.Comment(3), cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(beforeThird).IsEqualTo(0);
			await Assert.That((writer.WrittenCount, writer.PendingCount)).IsEqualTo((3L, 0));
			await Assert.That(writer.FlushDueIn).IsNull();
		}
	}

	[Test]
	public async Task FlushesByTimeEvenWhenNothingElseArrivesAsync(CancellationToken cancellationToken)
	{
		MemoryStream stream = new();
		FakeTimeProvider time = new();

		await using DanmakuXmlWriter writer = await DanmakuXmlWriter.CreateAsync(stream, DanmakuSamples.Header, new DanmakuFlushPolicy(TimeSpan.FromSeconds(5), 100), time, cancellationToken);
		await writer.WriteAsync(DanmakuSamples.Comment(1), cancellationToken);
		TimeSpan? dueAfterWrite = writer.FlushDueIn;
		time.Advance(TimeSpan.FromSeconds(3));
		await writer.FlushIfDueAsync(cancellationToken);
		(long Written, TimeSpan? Due) early = (writer.WrittenCount, writer.FlushDueIn);
		time.Advance(TimeSpan.FromSeconds(2));
		await writer.FlushIfDueAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(dueAfterWrite).IsEqualTo(TimeSpan.FromSeconds(5));
			await Assert.That(early).IsEqualTo((0L, TimeSpan.FromSeconds(2)));
			await Assert.That(writer.WrittenCount).IsEqualTo(1);
			await Assert.That(DanmakuSamples.ParseStrictly(stream.ToArray()).GetValueOrDefault(@"d")).IsEqualTo(1);
		}
	}

	[Test]
	public async Task WriteFlushesWhenTheOldestPendingRecordIsDueAsync(CancellationToken cancellationToken)
	{
		FakeTimeProvider time = new();

		await using DanmakuXmlWriter writer = await DanmakuXmlWriter.CreateAsync(new MemoryStream(), DanmakuSamples.Header, new DanmakuFlushPolicy(TimeSpan.FromSeconds(5), 100), time, cancellationToken);
		await writer.WriteAsync(DanmakuSamples.Comment(1), cancellationToken);
		time.Advance(TimeSpan.FromSeconds(5));
		await writer.WriteAsync(DanmakuSamples.Comment(2), cancellationToken);

		await Assert.That(writer.WrittenCount).IsEqualTo(2);
	}

	[Test]
	public async Task CommentFieldsFollowTheBilibiliLayoutAsync(CancellationToken cancellationToken)
	{
		byte[] content = await DanmakuSamples.WriteAsync(
		[
			new DanmakuRecord.Comment(TimeSpan.FromMilliseconds(12345), 1, 25, 16777215, DanmakuSamples.SentAt, 123, @"昵称", @"文本", 40685126698926083),
			new DanmakuRecord.Comment(TimeSpan.FromMilliseconds(500), 5, 36, 0xFF0000, DanmakuSamples.SentAt.AddMilliseconds(999), 0, @"淡***", @"匿名", 0)
		], cancellationToken);

		await Assert.That(DanmakuSamples.Lines(content).Where(static line => line.StartsWith(@"  <d ", StringComparison.Ordinal))).IsEquivalentTo(["  <d p=\"12.345,1,25,16777215,1795343190,0,884863d2,40685126698926083\" uid=\"123\" user=\"昵称\">文本</d>", "  <d p=\"0.500,5,36,16711680,1795343190,0,0,0\" uid=\"0\" user=\"淡***\">匿名</d>"], CollectionOrdering.Matching);
	}

	[Test]
	public async Task PricesAreWrittenInThousandthsOfAYuanAsync(CancellationToken cancellationToken)
	{
		byte[] content = await DanmakuSamples.WriteAsync(DanmakuSamples.AllKinds(), cancellationToken);
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
	public async Task OffsetIsAddedAndNegativeTimesBecomeZeroAsync(CancellationToken cancellationToken)
	{
		byte[] later = await DanmakuSamples.WriteAsync([DanmakuSamples.Comment(1), new DanmakuRecord.Gap(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2))], cancellationToken, DanmakuSamples.Header with { Offset = TimeSpan.FromMilliseconds(2500) });
		byte[] earlier = await DanmakuSamples.WriteAsync([DanmakuSamples.Comment(1), DanmakuSamples.Comment(10)], cancellationToken, DanmakuSamples.Header with { Offset = TimeSpan.FromSeconds(-5) });

		using (Assert.Multiple())
		{
			await Assert.That(DanmakuSamples.Lines(later)).Contains(static line => line.StartsWith("  <d p=\"3.500,", StringComparison.Ordinal));
			await Assert.That(DanmakuSamples.Lines(later)).Contains("  <gap start=\"3.500\" end=\"4.500\" />");
			await Assert.That(DanmakuSamples.Lines(earlier)).Contains(static line => line.StartsWith("  <d p=\"0.000,", StringComparison.Ordinal));
			await Assert.That(DanmakuSamples.Lines(earlier)).Contains(static line => line.StartsWith("  <d p=\"5.000,", StringComparison.Ordinal));
		}
	}

	[Test]
	public async Task InvalidCharactersAreDroppedAndEachElementStaysOnOneLineAsync(CancellationToken cancellationToken)
	{
		const string text = "a\u0001b\nc\uD800d\uFFFE<&>\"\U0001F600";
		byte[] content = await DanmakuSamples.WriteAsync([DanmakuSamples.Comment(1, text), new DanmakuRecord.TitleChange(TimeSpan.Zero, "第一行\r\n第二行")], cancellationToken);
		DanmakuFile file = await DanmakuSamples.ReadAsync(content, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(DanmakuSamples.ParseStrictly(content).GetValueOrDefault(@"d")).IsEqualTo(1);
			await Assert.That(DanmakuSamples.Lines(content)).Count().IsEqualTo(7);
			await Assert.That(file.Records.OfType<DanmakuRecord.Comment>().Select(static comment => comment.Text)).IsEquivalentTo(["ab\ncd<&>\"\U0001F600"], CollectionOrdering.Matching);
			await Assert.That(file.Records.OfType<DanmakuRecord.TitleChange>().Select(static title => title.Title)).IsEquivalentTo(["第一行\r\n第二行"], CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task QuotesInAttributeValuesAreEscapedAsync(CancellationToken cancellationToken)
	{
		DanmakuRecord[] records =
		[
			new DanmakuRecord.Comment(TimeSpan.FromSeconds(1), 1, 25, 0xFFFFFF, DanmakuSamples.SentAt, 123, "\"引号\"昵称", @"文本", 0),
			new DanmakuRecord.Gift(TimeSpan.FromSeconds(2), 789, @"观众", "\"礼物\"", 1, 1000)
		];
		byte[] content = await DanmakuSamples.WriteAsync(records, cancellationToken);
		DanmakuFile file = await DanmakuSamples.ReadAsync(content, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(DanmakuSamples.ParseStrictly(content).GetValueOrDefault(@"gift")).IsEqualTo(1);
			await Assert.That(file.Records).IsEquivalentTo(records, EqualityComparer<DanmakuRecord>.Default, CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task NumbersDoNotDependOnTheCurrentCultureAsync(CancellationToken cancellationToken)
	{
		CultureInfo original = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(@"de-DE");

		try
		{
			byte[] content = await DanmakuSamples.WriteAsync([DanmakuSamples.Comment(12.345)], cancellationToken, DanmakuSamples.Header with { Offset = TimeSpan.FromMilliseconds(250) });
			DanmakuFile file = await DanmakuSamples.ReadAsync(content, cancellationToken);

			using (Assert.Multiple())
			{
				await Assert.That(DanmakuSamples.Lines(content)).Contains(static line => line.StartsWith("  <d p=\"12.595,", StringComparison.Ordinal));
				await Assert.That(DanmakuSamples.Lines(content)).Contains(static line => line.Contains("offset=\"0.250\"", StringComparison.Ordinal));
				await Assert.That(file.Records.Select(static record => record.Time)).IsEquivalentTo([TimeSpan.FromMilliseconds(12595)], CollectionOrdering.Matching);
			}
		}
		finally
		{
			CultureInfo.CurrentCulture = original;
		}
	}

	[Test]
	public async Task FileCanBeReadWhileRecordingAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine(@"录制.xml");

		await using DanmakuXmlWriter writer = await DanmakuXmlWriter.CreateFileAsync(path, DanmakuSamples.Header, new DanmakuFlushPolicy(TimeSpan.FromSeconds(5), 1), TimeProvider.System, cancellationToken);
		await writer.WriteAsync(DanmakuSamples.Comment(1), cancellationToken);
		DanmakuFile file = await DanmakuXmlReader.ReadFileAsync(path, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(file.IsComplete).IsTrue();
			await Assert.That(file.Records).Count().IsEqualTo(1);
		}
	}

	[Test]
	public async Task CreatingAFileNeverOverwritesAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine(@"已存在.xml");
		await File.WriteAllTextAsync(path, @"keep", cancellationToken);

		await Assert.That(async () => await DanmakuXmlWriter.CreateFileAsync(path, DanmakuSamples.Header, DanmakuFlushPolicy.Default, TimeProvider.System, cancellationToken)).Throws<IOException>();
		await Assert.That(await File.ReadAllTextAsync(path, cancellationToken)).IsEqualTo(@"keep");
	}

	[Test]
	public async Task AFileWhoseHeaderCannotBeWrittenIsRemovedAsync()
	{
		using TestDirectory directory = new();
		string path = directory.Combine(@"取消.xml");

		await Assert.That(async () => await DanmakuXmlWriter.CreateFileAsync(path, DanmakuSamples.Header, DanmakuFlushPolicy.Default, TimeProvider.System, new CancellationToken(true))).Throws<OperationCanceledException>();
		await Assert.That(File.Exists(path)).IsFalse();
	}

	private sealed class FailingStream(MemoryStream inner) : Stream
	{
		public bool FailNextWrite { get; set; }

		public override bool CanRead => false;

		public override bool CanSeek => true;

		public override bool CanWrite => true;

		public override long Length => inner.Length;

		public override long Position
		{
			get => inner.Position;
			set => inner.Position = value;
		}

		public override void Flush()
		{
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException();
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			return inner.Seek(offset, origin);
		}

		public override void SetLength(long value)
		{
			inner.SetLength(value);
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			if (FailNextWrite)
			{
				FailNextWrite = false;
				inner.Write(buffer, offset, count / 2);
				throw new IOException(@"Disk full");
			}

			inner.Write(buffer, offset, count);
		}
	}
}
