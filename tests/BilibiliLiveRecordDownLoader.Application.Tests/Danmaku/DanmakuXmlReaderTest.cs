using BilibiliLive.Danmu;
using BilibiliLiveRecordDownLoader.Application.Danmaku;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Danmaku;

public sealed class DanmakuXmlReaderTest
{
	private static IEnumerable<string> CommentTexts(DanmakuFile file)
	{
		return file.Records.OfType<DanmakuRecord.Comment>().Select(static comment => comment.Text);
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task ReadsBackEverythingItWroteAsync(bool canSeek, CancellationToken cancellationToken)
	{
		DanmakuRecord[] records = DanmakuSamples.AllKinds();
		byte[] content = await DanmakuSamples.WriteAsync(records, cancellationToken);
		await using Stream stream = canSeek ? new MemoryStream(content) : new ForwardOnlyStream(new MemoryStream(content));

		DanmakuFile file = await DanmakuXmlReader.ReadAsync(stream, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(file.IsComplete).IsTrue();
			await Assert.That(file.Source).IsEqualTo(new DanmakuFileSource.Native(DanmakuSamples.Header));
			await Assert.That(file.Records).IsEquivalentTo(records, EqualityComparer<DanmakuRecord>.Default, CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task TruncatedFilesYieldTheRecordsBeforeTheCutAsync(CancellationToken cancellationToken)
	{
		DanmakuRecord[] records = DanmakuSamples.AllKinds();
		byte[] content = await DanmakuSamples.WriteAsync(records, cancellationToken);
		int previousCount = 0;

		// 最后一个字节是 </i> 后的换行，少了它文件仍然完整
		for (int length = 0; length < content.Length - 1; ++length)
		{
			DanmakuFile file = await DanmakuSamples.ReadAsync([.. content.AsSpan(0, length)], cancellationToken);

			await Assert.That(file.IsComplete).IsFalse();
			await Assert.That(file.Records.Count).IsGreaterThanOrEqualTo(previousCount);
			await Assert.That(file.Records).IsEquivalentTo(records.Take(file.Records.Count), EqualityComparer<DanmakuRecord>.Default, CollectionOrdering.Matching);
			previousCount = file.Records.Count;
		}

		await Assert.That(previousCount).IsEqualTo(records.Length);
	}

	[Test]
	public async Task ReadsBilibiliVideoDanmakuAsync(CancellationToken cancellationToken)
	{
		const string xml = """
			<?xml version="1.0" encoding="UTF-8"?><i><chatserver>chat.bilibili.com</chatserver><chatid>1</chatid><mission>0</mission><maxlimit>1000</maxlimit><state>0</state><real_name>0</real_name><source>k-v</source><d p="0.60100,4,25,15138834,1604742895,0,9e5adeaa,40685126698926083,10">底部弹幕</d><d p="9.41600,5,25,16755202,1611378869,0,14f71d3,44164288244350983,10">顶部弹幕</d></i>
			""";

		DanmakuFile file = await DanmakuSamples.ReadAsync(xml, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(file.IsComplete).IsTrue();
			await Assert.That(file.Source).IsTypeOf<DanmakuFileSource.Standard>();
			await Assert.That(file.Records).IsEquivalentTo(
			[
				new DanmakuRecord.Comment(TimeSpan.FromMilliseconds(601), 4, 25, 15138834, DateTimeOffset.FromUnixTimeSeconds(1604742895), 0, string.Empty, @"底部弹幕", 40685126698926083) { UidHash = @"9e5adeaa" },
				new DanmakuRecord.Comment(TimeSpan.FromMilliseconds(9416), 5, 25, 16755202, DateTimeOffset.FromUnixTimeSeconds(1611378869), 0, string.Empty, @"顶部弹幕", 44164288244350983) { UidHash = @"14f71d3" }
			], EqualityComparer<DanmakuRecord>.Default, CollectionOrdering.Matching);
		}
	}

	// 内嵌的 XSL 里的 HTML 元素也叫 title 之类
	[Test]
	public async Task OtherFilesYieldOnlyCommentsAsync(CancellationToken cancellationToken)
	{
		const string xml = """
			<?xml version="1.0" encoding="utf-8"?>
			<?xml-stylesheet type="text/xsl" href="#s"?>
			<i>
			<chatserver>chat.bilibili.com</chatserver><chatid>0</chatid><mission>0</mission><maxlimit>1000</maxlimit><state>0</state><real_name>0</real_name><source>0</source>
			<OtherRecorder version="2.0.0" />
			<OtherRecordInfo roomid="1" name="主播" title="标题" />
			<OtherXmlStyle><z:stylesheet version="1.0" id="s" xml:id="s" xmlns:z="http://www.w3.org/1999/XSL/Transform"><z:template match="/"><html><head><title><z:value-of select="i/OtherRecordInfo/@name" /></title></head></html></z:template></z:stylesheet></OtherXmlStyle>
			<d p="1.234,1,25,16777215,1795343190,0,a16fe0dd,5e7c" uid="12345" user="观众" storm="1">弹幕</d>
			<sc ts="5.000" user="老板" uid="12345" price="30" time="60">醒目留言</sc>
			<gift ts="6.000" user="观众" uid="12345" giftname="辣条" giftcount="3" />
			<guard ts="7.000" user="舰长" uid="12345" level="3" count="2" />
			<title ts="8.000">标题</title>
			</i>
			""";

		DanmakuFile file = await DanmakuSamples.ReadAsync(xml, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(file.IsComplete).IsTrue();
			await Assert.That(file.Source).IsTypeOf<DanmakuFileSource.Standard>();
			// p[7] 不是十进制的 dmid 时读成 0；B 站标准的 d 只有 p，其他程序写在 d 上的 uid、user 和 storm 不读
			await Assert.That(file.Records).IsEquivalentTo([new DanmakuRecord.Comment(TimeSpan.FromMilliseconds(1234), 1, 25, 16777215, DanmakuSamples.SentAt, 0, string.Empty, @"弹幕", 0) { UidHash = @"a16fe0dd" }], EqualityComparer<DanmakuRecord>.Default);
		}
	}

	[Test]
	[Arguments("</d>", 0)]
	[Arguments("</sc>", 1)]
	[Arguments("</title>", 2)]
	public async Task RecordEndingRightAtTheCutIsKeptAsync(string closingTag, int index, CancellationToken cancellationToken)
	{
		DanmakuRecord[] records =
		[
			DanmakuSamples.Comment(1, @"弹幕"),
			new DanmakuRecord.SuperChat(TimeSpan.FromSeconds(2), 456, @"老板", 30000, TimeSpan.FromSeconds(60), @"醒目留言") { Id = 1 },
			new DanmakuRecord.TitleChange(TimeSpan.FromSeconds(3), @"新标题")
		];
		byte[] content = await DanmakuSamples.WriteAsync(records, cancellationToken);
		byte[] tag = Encoding.UTF8.GetBytes(closingTag);
		int cut = content.AsSpan().IndexOf(tag) + tag.Length;

		DanmakuFile file = await DanmakuSamples.ReadAsync([.. content.AsSpan(0, cut)], cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(file.IsComplete).IsFalse();
			await Assert.That(file.Records).IsEquivalentTo(records.Take(index + 1), EqualityComparer<DanmakuRecord>.Default, CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task CommentWithChildElementsDoesNotStopTheFileAsync(CancellationToken cancellationToken)
	{
		const string xml = """
			<?xml version="1.0" encoding="utf-8"?>
			<i>
			  <d p="1,1,25,16777215,0,0,0,0">前面<b>子元素</b>后面</d>
			  <d p="2,1,25,16777215,0,0,0,0">下一条</d>
			</i>
			""";

		DanmakuFile file = await DanmakuSamples.ReadAsync(xml, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(file.IsComplete).IsTrue();
			await Assert.That(CommentTexts(file)).IsEquivalentTo([@"前面", @"下一条"], CollectionOrdering.Matching);
		}
	}

	// 按坏数据分格会抛异常或分配巨大的数组；自己的文件来自不分段的仅弹幕录制时可能超过一天
	[Test]
	public async Task ImplausibleTimesAreSkippedAsync(CancellationToken cancellationToken)
	{
		const string other = """
			<?xml version="1.0" encoding="utf-8"?>
			<i>
			  <d p="-10,1,25,16777215,0,0,0,0">负数</d>
			  <d p="90000,1,25,16777215,0,0,0,0">超过一天</d>
			  <d p="922337203685.4776,1,25,16777215,0,0,0,0">超出范围</d>
			  <d p="86400,1,25,16777215,0,0,0,0">正好一天</d>
			</i>
			""";
		const string native = """
			<?xml version="1.0" encoding="utf-8"?>
			<i>
			  <BilibiliLiveRecordDownLoader version="1.0" offset="-5.000" />
			  <d p="-1,1,25,16777215,0,0,0,0">负数</d>
			  <d p="90000,1,25,16777215,0,0,0,0">超过一天</d>
			  <gap start="-1.000" end="2.000" />
			  <title ts="1000000000">太大</title>
			</i>
			""";

		DanmakuFile standardFile = await DanmakuSamples.ReadAsync(other, cancellationToken);
		DanmakuFile nativeFile = await DanmakuSamples.ReadAsync(native, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(standardFile.IsComplete).IsTrue();
			await Assert.That(CommentTexts(standardFile)).IsEquivalentTo([@"正好一天"]);
			await Assert.That(nativeFile.Source).IsTypeOf<DanmakuFileSource.Native>().And.Member(static source => source.Header.Offset, static offset => offset.IsEqualTo(TimeSpan.FromSeconds(-5)));
			await Assert.That(nativeFile.Records).Count().IsEqualTo(1);
			await Assert.That(CommentTexts(nativeFile)).IsEquivalentTo([@"超过一天"]);
		}
	}

	[Test]
	[Arguments("3", GuardLevel.Captain)]
	[Arguments("7", GuardLevel.None)]
	[Arguments("x", GuardLevel.None)]
	public async Task UnknownGuardLevelsAreReadAsNoneAsync(string level, GuardLevel expected, CancellationToken cancellationToken)
	{
		string xml = $"""
			<?xml version="1.0" encoding="utf-8"?>
			<i>
			  <BilibiliLiveRecordDownLoader version="1.0" />
			  <guard ts="1.000" uid="1" user="舰长" level="{level}" count="1" price="198000" />
			</i>
			""";

		DanmakuFile file = await DanmakuSamples.ReadAsync(xml, cancellationToken);

		await Assert.That(file.Records.OfType<DanmakuRecord.Guard>().Select(static guard => guard.Level)).IsEquivalentTo([expected]);
	}

	[Test]
	public async Task SkipsUnknownAndMalformedElementsAsync(CancellationToken cancellationToken)
	{
		const string xml = """
			<?xml version="1.0" encoding="utf-8"?>
			<i>
			  <unknown a="1"><d p="1,1,25,0,0,0,0,0">嵌套的不算</d></unknown>
			  <d>没有 p</d>
			  <d p="abc,1,25,0,0,0,0,0">时间不对</d>
			  <title>没有时间<b>带子元素</b></title>
			  <d p="2.5">字段不全</d>
			</i>
			""";

		DanmakuFile file = await DanmakuSamples.ReadAsync(xml, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(file.IsComplete).IsTrue();
			// 缺的字段取默认值；没有 p[6] 时哈希读成 0，不按 uid 现算
			await Assert.That(file.Records).IsEquivalentTo([new DanmakuRecord.Comment(TimeSpan.FromMilliseconds(2500), 1, 25, 0xFFFFFF, DateTimeOffset.UnixEpoch, 0, string.Empty, @"字段不全", 0) { UidHash = @"0" }], EqualityComparer<DanmakuRecord>.Default);
		}
	}

	[Test]
	public async Task GarbageIsAnIncompleteEmptyFileAsync(CancellationToken cancellationToken)
	{
		DanmakuFile file = await DanmakuSamples.ReadAsync(@"not xml at all", cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(file.IsComplete).IsFalse();
			await Assert.That(file.Records).IsEmpty();
			await Assert.That(file.Source).IsTypeOf<DanmakuFileSource.Standard>();
		}
	}

	[Test]
	public async Task SuperChatDeletionsKeepOnlyValidIdsAsync(CancellationToken cancellationToken)
	{
		const string xml = """
			<?xml version="1.0" encoding="utf-8"?>
			<i>
			  <BilibiliLiveRecordDownLoader version="1.0" />
			  <sc ts="1.000" uid="1" user="老板" price="30000" time="60">没有 id</sc>
			  <sc_delete ts="2.000" ids="0,x" />
			  <sc_delete ts="3.000" />
			  <sc_delete ts="4.000" ids="5,0,6" />
			</i>
			""";

		DanmakuFile file = await DanmakuSamples.ReadAsync(xml, cancellationToken);

		await Assert.That(file.Records).IsEquivalentTo(
		[
			new DanmakuRecord.SuperChat(TimeSpan.FromSeconds(1), 1, @"老板", 30000, TimeSpan.FromSeconds(60), @"没有 id"),
			new DanmakuRecord.SuperChatDeletion(TimeSpan.FromSeconds(4), [5, 6])
		], EqualityComparer<DanmakuRecord>.Default, CollectionOrdering.Matching);
	}

	private sealed class ForwardOnlyStream(Stream inner) : Stream
	{
		public override bool CanRead => true;

		public override bool CanSeek => false;

		public override bool CanWrite => false;

		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override void Flush()
		{
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			return inner.Read(buffer, offset, count);
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			throw new NotSupportedException();
		}

		public override void SetLength(long value)
		{
			throw new NotSupportedException();
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException();
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				inner.Dispose();
			}

			base.Dispose(disposing);
		}
	}
}
