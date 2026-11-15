namespace BilibiliLiveRecordDownLoader.Application.Tests.Domain;

public sealed class StreamPriorityTest
{
	[Test]
	public async Task NormalizeKeepsOrderAndDropsDuplicatesAndUndefinedValuesAsync()
	{
		List<StreamCodec> result = StreamPriority.Normalize([StreamCodec.Hevc, (StreamCodec)42, StreamCodec.Avc, StreamCodec.Hevc], StreamPriority.DefaultCodecs);

		await Assert.That(result.SequenceEqual([StreamCodec.Hevc, StreamCodec.Avc])).IsTrue();
	}

	[Test]
	public async Task NormalizeFallsBackWhenNothingIsLeftAsync()
	{
		List<StreamFormat> fromNull = StreamPriority.Normalize(null, StreamPriority.DefaultFormats);
		List<StreamFormat> fromInvalid = StreamPriority.Normalize([(StreamFormat)42], StreamPriority.DefaultFormats);

		await Assert.That(fromNull.SequenceEqual(StreamPriority.DefaultFormats)).IsTrue();
		await Assert.That(fromInvalid.SequenceEqual(StreamPriority.DefaultFormats)).IsTrue();
	}
}
