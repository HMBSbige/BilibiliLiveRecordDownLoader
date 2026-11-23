using BilibiliLiveRecordDownLoader.Application.Danmaku;
using TUnit.Assertions.Enums;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Danmaku;

public sealed class DanmakuOffsetRewriterTest
{
	[Test]
	public async Task ShiftsEveryRecordByTheDifferenceAndKeepsTheRestAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine(@"弹幕.xml");
		DanmakuRecord[] records = DanmakuSamples.AllKinds();
		await File.WriteAllBytesAsync(path, await DanmakuSamples.WriteAsync(records, DanmakuSamples.Header with { Offset = TimeSpan.FromSeconds(1) }), cancellationToken);

		await DanmakuOffsetRewriter.RewriteAsync(path, TimeSpan.FromMilliseconds(3500), cancellationToken);
		DanmakuFile file = await DanmakuXmlReader.ReadFileAsync(path, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(file.Source).IsEqualTo(new DanmakuFileSource.Native(DanmakuSamples.Header with { Offset = TimeSpan.FromMilliseconds(3500) }));
			await Assert.That(file.Records).IsEquivalentTo(records.Select(static record => record.Shift(TimeSpan.FromMilliseconds(3500))), EqualityComparer<DanmakuRecord>.Default, CollectionOrdering.Matching);
			await Assert.That(Directory.GetFiles(directory.Root)).IsEquivalentTo([path]);
		}
	}

	[Test]
	public async Task RecordsPushedBelowZeroStayAtZeroAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine(@"弹幕.xml");
		await File.WriteAllBytesAsync(path, await DanmakuSamples.WriteAsync([DanmakuSamples.Comment(1), DanmakuSamples.Comment(10)]), cancellationToken);

		await DanmakuOffsetRewriter.RewriteAsync(path, TimeSpan.FromSeconds(-2), cancellationToken);
		DanmakuFile file = await DanmakuXmlReader.ReadFileAsync(path, cancellationToken);

		await Assert.That(file.Records.Select(static record => record.Time)).IsEquivalentTo([TimeSpan.Zero, TimeSpan.FromSeconds(8)], CollectionOrdering.Matching);
	}

	// 字幕用返回的记录生成，要和改写后的文件一致：推到 0 之前的写成 0，偏移不是整毫秒时截到毫秒
	[Test]
	[Arguments(-20_000_000L)]
	[Arguments(35_009_999L)]
	public async Task ReturnsTheRecordsAsTheRewrittenFileHoldsThemAsync(long offsetTicks, CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine(@"弹幕.xml");
		await File.WriteAllBytesAsync(path, await DanmakuSamples.WriteAsync([.. DanmakuSamples.AllKinds(), new DanmakuRecord.Gap(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1500))]), cancellationToken);

		IReadOnlyList<DanmakuRecord> records = await DanmakuOffsetRewriter.RewriteAsync(path, TimeSpan.FromTicks(offsetTicks), cancellationToken);
		DanmakuFile file = await DanmakuXmlReader.ReadFileAsync(path, cancellationToken);

		await Assert.That(records).IsEquivalentTo(file.Records, EqualityComparer<DanmakuRecord>.Default, CollectionOrdering.Matching);
	}

	[Test]
	public async Task RefusesFilesFromOtherProgramsAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine(@"别人的.xml");
		const string xml = """<?xml version="1.0" encoding="UTF-8"?><i><d p="1.0,1,25,16777215,0,0,0,0">弹幕</d></i>""";
		await File.WriteAllTextAsync(path, xml, cancellationToken);

		await Assert.That(async () => await DanmakuOffsetRewriter.RewriteAsync(path, TimeSpan.FromSeconds(1), cancellationToken)).Throws<InvalidDataException>();

		using (Assert.Multiple())
		{
			await Assert.That(await File.ReadAllTextAsync(path, cancellationToken)).IsEqualTo(xml);
			await Assert.That(Directory.GetFiles(directory.Root)).IsEquivalentTo([path]);
		}
	}
}
