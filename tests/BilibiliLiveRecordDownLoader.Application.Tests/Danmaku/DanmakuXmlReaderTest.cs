using BilibiliLive.Danmu;
using BilibiliLiveRecordDownLoader.Application.Danmaku;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Danmaku;

public sealed class DanmakuXmlReaderTest
{
	[Test]
	public async Task ReadsBackEverythingItWroteAsync()
	{
		DanmakuRecord[] records = DanmakuSamples.AllKinds();
		DanmakuFile file = await DanmakuSamples.ReadAsync(await DanmakuSamples.WriteAsync(records));

		using (Assert.Multiple())
		{
			await Assert.That(file.IsComplete).IsTrue();
			await Assert.That(file.Source).IsEqualTo(new DanmakuFileSource.Native(DanmakuSamples.Header));
			await Assert.That(file.Records).IsEquivalentTo(records, EqualityComparer<DanmakuRecord>.Default, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task TruncatedFilesYieldTheRecordsBeforeTheCutAsync()
	{
		DanmakuRecord[] records = DanmakuSamples.AllKinds();
		byte[] content = await DanmakuSamples.WriteAsync(records);
		int previousCount = 0;

		// 最后一个字节是 </i> 后的换行，少了它文件仍然完整
		for (int length = 0; length < content.Length - 1; ++length)
		{
			DanmakuFile file = await DanmakuSamples.ReadAsync(content.AsSpan(0, length).ToArray());

			await Assert.That(file.IsComplete).IsFalse();
			await Assert.That(file.Records.Count).IsGreaterThanOrEqualTo(previousCount);
			await Assert.That(file.Records).IsEquivalentTo(records.Take(file.Records.Count), EqualityComparer<DanmakuRecord>.Default, TUnit.Assertions.Enums.CollectionOrdering.Matching);
			previousCount = file.Records.Count;
		}

		await Assert.That(previousCount).IsEqualTo(records.Length);
	}

	[Test]
	public async Task ReadsBilibiliVideoDanmakuAsync()
	{
		const string xml = """
			<?xml version="1.0" encoding="UTF-8"?><i><chatserver>chat.bilibili.com</chatserver><chatid>1</chatid><mission>0</mission><maxlimit>1000</maxlimit><state>0</state><real_name>0</real_name><source>k-v</source><d p="0.60100,4,25,15138834,1604742895,0,9e5adeaa,40685126698926083,10">底部弹幕</d><d p="9.41600,5,25,16755202,1611378869,0,14f71d3,44164288244350983,10">顶部弹幕</d></i>
			""";

		DanmakuFile file = await DanmakuSamples.ReadAsync(xml);
		DanmakuRecord.Comment[] comments = file.Records.OfType<DanmakuRecord.Comment>().ToArray();

		using (Assert.Multiple())
		{
			await Assert.That(file.IsComplete).IsTrue();
			await Assert.That(file.Source).IsTypeOf<DanmakuFileSource.Standard>();
			await Assert.That(comments.Length).IsEqualTo(2);
			await Assert.That(comments[0].Time).IsEqualTo(TimeSpan.FromMilliseconds(601));
			await Assert.That(comments[0].Mode).IsEqualTo(4);
			await Assert.That(comments[0].FontSize).IsEqualTo(25);
			await Assert.That(comments[0].Color).IsEqualTo(15138834);
			await Assert.That(comments[0].SentAt).IsEqualTo(DateTimeOffset.FromUnixTimeSeconds(1604742895));
			await Assert.That(comments[0].Uid).IsEqualTo(0);
			await Assert.That(comments[0].UidHash).IsEqualTo(@"9e5adeaa");
			await Assert.That(comments[0].DmId).IsEqualTo(40685126698926083);
			await Assert.That(comments[0].Text).IsEqualTo(@"底部弹幕");
			await Assert.That(comments[1].UidHash).IsEqualTo(@"14f71d3");
		}
	}

	// 其他程序的扩展元素含义和单位各不相同；内嵌的 XSL 里的 HTML 元素也叫 title 之类
	[Test]
	public async Task OtherFilesYieldOnlyCommentsAsync()
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

		DanmakuFile file = await DanmakuSamples.ReadAsync(xml);
		DanmakuRecord.Comment comment = file.Records.OfType<DanmakuRecord.Comment>().Single();

		using (Assert.Multiple())
		{
			await Assert.That(file.IsComplete).IsTrue();
			await Assert.That(file.Source).IsTypeOf<DanmakuFileSource.Standard>();
			await Assert.That(file.Records.Count).IsEqualTo(1);
			await Assert.That(comment.Time).IsEqualTo(TimeSpan.FromMilliseconds(1234));
			await Assert.That(comment.SentAt).IsEqualTo(DateTimeOffset.FromUnixTimeSeconds(1795343190));
			await Assert.That(comment.UidHash).IsEqualTo(@"a16fe0dd");
			// p[7] 不是十进制的 dmid 时读成 0
			await Assert.That(comment.DmId).IsEqualTo(0);
			await Assert.That(comment.Text).IsEqualTo(@"弹幕");
			// B 站标准的 d 只有 p，其他程序写在 d 上的同名属性不读
			await Assert.That(comment.Uid).IsEqualTo(0);
			await Assert.That(comment.User).IsEmpty();
			await Assert.That(comment.IsStorm).IsFalse();
		}
	}

	[Test]
	[Arguments("</d>", 0)]
	[Arguments("</sc>", 1)]
	[Arguments("</title>", 2)]
	public async Task RecordEndingRightAtTheCutIsKeptAsync(string closingTag, int index)
	{
		DanmakuRecord[] records =
		[
			DanmakuSamples.Comment(1, @"弹幕"),
			new DanmakuRecord.SuperChat(TimeSpan.FromSeconds(2), 456, @"老板", 30000, TimeSpan.FromSeconds(60), @"醒目留言") { Id = 1 },
			new DanmakuRecord.TitleChange(TimeSpan.FromSeconds(3), @"新标题")
		];
		byte[] content = await DanmakuSamples.WriteAsync(records);
		byte[] tag = Encoding.UTF8.GetBytes(closingTag);
		int cut = content.AsSpan().IndexOf(tag) + tag.Length;

		DanmakuFile file = await DanmakuSamples.ReadAsync(content.AsSpan(0, cut).ToArray());

		using (Assert.Multiple())
		{
			await Assert.That(file.IsComplete).IsFalse();
			await Assert.That(file.Records).IsEquivalentTo(records.Take(index + 1), EqualityComparer<DanmakuRecord>.Default, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task CommentWithChildElementsDoesNotStopTheFileAsync()
	{
		const string xml = """
			<?xml version="1.0" encoding="utf-8"?>
			<i>
			  <d p="1,1,25,16777215,0,0,0,0">前面<b>子元素</b>后面</d>
			  <d p="2,1,25,16777215,0,0,0,0">下一条</d>
			</i>
			""";

		DanmakuFile file = await DanmakuSamples.ReadAsync(xml);

		using (Assert.Multiple())
		{
			await Assert.That(file.IsComplete).IsTrue();
			await Assert.That(file.Records.OfType<DanmakuRecord.Comment>().Select(static comment => comment.Text).ToArray()).IsEquivalentTo([@"前面", @"下一条"], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		}
	}

	// 按坏数据分格会抛异常或分配巨大的数组；自己的文件来自不分段的仅弹幕录制时可能超过一天
	[Test]
	public async Task ImplausibleTimesAreSkippedAsync()
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

		DanmakuFile standardFile = await DanmakuSamples.ReadAsync(other);
		DanmakuFile nativeFile = await DanmakuSamples.ReadAsync(native);

		using (Assert.Multiple())
		{
			await Assert.That(standardFile.IsComplete).IsTrue();
			await Assert.That(standardFile.Records.OfType<DanmakuRecord.Comment>().Select(static comment => comment.Text).Single()).IsEqualTo(@"正好一天");
			await Assert.That(nativeFile.Source is DanmakuFileSource.Native { Header.Offset.TotalSeconds: -5 }).IsTrue();
			await Assert.That(nativeFile.Records.OfType<DanmakuRecord.Comment>().Select(static comment => comment.Text).Single()).IsEqualTo(@"超过一天");
			await Assert.That(nativeFile.Records.Count).IsEqualTo(1);
		}
	}

	[Test]
	[Arguments("3", GuardLevel.Captain)]
	[Arguments("7", GuardLevel.None)]
	[Arguments("x", GuardLevel.None)]
	public async Task UnknownGuardLevelsAreReadAsNoneAsync(string level, GuardLevel expected)
	{
		string xml = $"""
			<?xml version="1.0" encoding="utf-8"?>
			<i>
			  <BilibiliLiveRecordDownLoader version="1.0" />
			  <guard ts="1.000" uid="1" user="舰长" level="{level}" count="1" price="198000" />
			</i>
			""";

		DanmakuFile file = await DanmakuSamples.ReadAsync(xml);

		await Assert.That(file.Records.OfType<DanmakuRecord.Guard>().Single().Level).IsEqualTo(expected);
	}

	[Test]
	public async Task ReadsStreamsThatCannotSeekAsync()
	{
		byte[] content = await DanmakuSamples.WriteAsync(DanmakuSamples.AllKinds());
		await using ForwardOnlyStream stream = new(new MemoryStream(content));

		DanmakuFile file = await DanmakuXmlReader.ReadAsync(stream, CancellationToken.None);

		using (Assert.Multiple())
		{
			await Assert.That(file.IsComplete).IsTrue();
			await Assert.That(file.Records.Count).IsEqualTo(DanmakuSamples.AllKinds().Length);
		}
	}

	[Test]
	public async Task SkipsUnknownAndMalformedElementsAsync()
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

		DanmakuFile file = await DanmakuSamples.ReadAsync(xml);
		DanmakuRecord.Comment comment = file.Records.OfType<DanmakuRecord.Comment>().Single();

		using (Assert.Multiple())
		{
			await Assert.That(file.IsComplete).IsTrue();
			await Assert.That(file.Records.Count).IsEqualTo(1);
			await Assert.That(comment.Time).IsEqualTo(TimeSpan.FromMilliseconds(2500));
			await Assert.That(comment.Mode).IsEqualTo(1);
			await Assert.That(comment.FontSize).IsEqualTo(25);
			await Assert.That(comment.Color).IsEqualTo(0xFFFFFF);
			await Assert.That(comment.Text).IsEqualTo(@"字段不全");
		}
	}

	[Test]
	public async Task GarbageIsAnIncompleteEmptyFileAsync()
	{
		DanmakuFile file = await DanmakuSamples.ReadAsync(@"not xml at all");

		using (Assert.Multiple())
		{
			await Assert.That(file.IsComplete).IsFalse();
			await Assert.That(file.Records).IsEmpty();
			await Assert.That(file.Source).IsTypeOf<DanmakuFileSource.Standard>();
		}
	}

	// id 为 0 表示没有 id，留着会对上所有没记 id 的 SC
	[Test]
	public async Task SuperChatDeletionsKeepOnlyValidIdsAsync()
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

		DanmakuFile file = await DanmakuSamples.ReadAsync(xml);

		using (Assert.Multiple())
		{
			await Assert.That(file.Records.Count).IsEqualTo(2);
			await Assert.That(file.Records[0] is DanmakuRecord.SuperChat { Id: 0, Text: "没有 id" }).IsTrue();
			await Assert.That(file.Records[1]).IsEqualTo(new DanmakuRecord.SuperChatDeletion(TimeSpan.FromSeconds(4), [5, 6]));
		}
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
