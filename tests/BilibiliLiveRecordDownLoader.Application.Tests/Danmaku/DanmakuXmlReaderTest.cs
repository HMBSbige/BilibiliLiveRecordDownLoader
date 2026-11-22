using BilibiliLiveRecordDownLoader.Application.Danmaku;

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

	// 每个截断位置都不能抛异常，读到的必须是原记录的前缀
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
			await Assert.That(comments[0].DmId).IsEqualTo(@"40685126698926083");
			await Assert.That(comments[0].Text).IsEqualTo(@"底部弹幕");
			await Assert.That(comments[1].UidHash).IsEqualTo(@"14f71d3");
		}
	}

	// 其他程序的扩展元素含义和单位各不相同，只读 d；内嵌的 XSL 里的 HTML 元素也叫 title 之类，不能当成记录
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
			<d p="1.234,1,25,16777215,1795343190,0,a16fe0dd,5e7c" user="观众">弹幕</d>
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
			await Assert.That(comment.DmId).IsEqualTo(@"5e7c");
			await Assert.That(comment.Text).IsEqualTo(@"弹幕");
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
}
