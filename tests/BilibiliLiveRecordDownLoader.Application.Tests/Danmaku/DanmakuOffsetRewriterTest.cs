using BilibiliLiveRecordDownLoader.Application.Danmaku;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Danmaku;

public sealed class DanmakuOffsetRewriterTest
{
	[Test]
	public async Task ShiftsEveryRecordByTheDifferenceAndKeepsTheRestAsync()
	{
		using TestDirectory directory = new();
		string path = directory.Combine(@"弹幕.xml");
		DanmakuRecord[] records = DanmakuSamples.AllKinds();
		await File.WriteAllBytesAsync(path, await DanmakuSamples.WriteAsync(records, DanmakuSamples.Header with { Offset = TimeSpan.FromSeconds(1) }));

		DanmakuFileHeader header = await DanmakuOffsetRewriter.RewriteAsync(path, TimeSpan.FromMilliseconds(3500), CancellationToken.None);
		DanmakuFile file = await DanmakuXmlReader.ReadFileAsync(path, CancellationToken.None);

		using (Assert.Multiple())
		{
			await Assert.That(header).IsEqualTo(DanmakuSamples.Header with { Offset = TimeSpan.FromMilliseconds(3500) });
			await Assert.That(file.Source).IsEqualTo(new DanmakuFileSource.Native(header));
			await Assert.That(file.Records).IsEquivalentTo(records.Select(static record => record.Shift(TimeSpan.FromMilliseconds(3500))), EqualityComparer<DanmakuRecord>.Default, TUnit.Assertions.Enums.CollectionOrdering.Matching);
			await Assert.That(Directory.GetFiles(directory.Root)).IsEquivalentTo([path], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task RecordsPushedBelowZeroStayAtZeroAsync()
	{
		using TestDirectory directory = new();
		string path = directory.Combine(@"弹幕.xml");
		await File.WriteAllBytesAsync(path, await DanmakuSamples.WriteAsync([DanmakuSamples.Comment(1), DanmakuSamples.Comment(10)]));

		await DanmakuOffsetRewriter.RewriteAsync(path, TimeSpan.FromSeconds(-2), CancellationToken.None);
		DanmakuFile file = await DanmakuXmlReader.ReadFileAsync(path, CancellationToken.None);

		await Assert.That(file.Records.Select(static record => record.Time)).IsEquivalentTo([TimeSpan.Zero, TimeSpan.FromSeconds(8)], EqualityComparer<TimeSpan>.Default, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task RefusesFilesFromOtherProgramsAsync()
	{
		using TestDirectory directory = new();
		string path = directory.Combine(@"别人的.xml");
		const string xml = """<?xml version="1.0" encoding="UTF-8"?><i><d p="1.0,1,25,16777215,0,0,0,0">弹幕</d></i>""";
		await File.WriteAllTextAsync(path, xml);

		await Assert.That(async () => await DanmakuOffsetRewriter.RewriteAsync(path, TimeSpan.FromSeconds(1), CancellationToken.None)).Throws<InvalidDataException>();
		await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(xml);
		await Assert.That(Directory.GetFiles(directory.Root)).IsEquivalentTo([path], StringComparer.Ordinal, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}
}
