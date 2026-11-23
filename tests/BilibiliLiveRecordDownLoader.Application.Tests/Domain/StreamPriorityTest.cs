using TUnit.Assertions.Enums;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Domain;

public sealed class StreamPriorityTest
{
	[Test]
	public async Task NormalizeKeepsOrderAndDropsDuplicatesAndUndefinedValuesAsync()
	{
		List<StreamCodec> result = StreamPriority.Normalize([StreamCodec.Hevc, (StreamCodec)42, StreamCodec.Avc, StreamCodec.Hevc], StreamPriority.DefaultCodecs);

		await Assert.That(result).IsEquivalentTo([StreamCodec.Hevc, StreamCodec.Avc], CollectionOrdering.Matching);
	}

	[Test]
	public async Task NormalizeFallsBackWhenNothingIsLeftAsync()
	{
		List<StreamFormat> fromNull = StreamPriority.Normalize(null, StreamPriority.DefaultFormats);
		List<StreamFormat> fromInvalid = StreamPriority.Normalize([(StreamFormat)42], StreamPriority.DefaultFormats);

		using (Assert.Multiple())
		{
			await Assert.That(fromNull).IsEquivalentTo(StreamPriority.DefaultFormats, CollectionOrdering.Matching);
			await Assert.That(fromInvalid).IsEquivalentTo(StreamPriority.DefaultFormats, CollectionOrdering.Matching);
		}
	}
}
