using MediaToolkit.Flv;
using System.Buffers;

namespace MediaToolkit.Tests.Flv;

public class AacAdtsConverterTest
{
	[Test]
	public async Task ClassifiesEachTagAsync()
	{
		AacAdtsConverter converter = new();

		FlvTagConversion rawBeforeSequenceHeader = Classify(converter, FlvSamples.AacFrame);
		FlvTagConversion sequenceHeader = Classify(converter, FlvSamples.AacSequenceHeader);
		FlvTagConversion frame = Classify(converter, FlvSamples.AacFrame);
		FlvTagConversion emptyFrame = Classify(converter, Convert.FromHexString(@"AF01"));
		FlvTagConversion truncated = Classify(converter, Convert.FromHexString(@"AF"));
		FlvTagConversion unknownPacketType = Classify(converter, Convert.FromHexString(@"AF02"));
		FlvTagConversion mp3 = Classify(converter, Convert.FromHexString(@"2F01" + @"21004990"));
		// AudioObjectType 6（BSAC）无法用 ADTS 表示
		FlvTagConversion unsupportedSequenceHeader = Classify(converter, Convert.FromHexString(@"AF00" + @"3210"));
		FlvTagConversion rawAfterUnsupportedSequenceHeader = Classify(converter, FlvSamples.AacFrame);

		using (Assert.Multiple())
		{
			await Assert.That(rawBeforeSequenceHeader).IsEqualTo(FlvTagConversion.Unsupported);
			await Assert.That(sequenceHeader).IsEqualTo(FlvTagConversion.NonFrame);
			await Assert.That(frame).IsEqualTo(FlvTagConversion.Frame);
			await Assert.That(emptyFrame).IsEqualTo(FlvTagConversion.NonFrame);
			await Assert.That(truncated).IsEqualTo(FlvTagConversion.NonFrame);
			await Assert.That(unknownPacketType).IsEqualTo(FlvTagConversion.Unsupported);
			await Assert.That(mp3).IsEqualTo(FlvTagConversion.Unsupported);
			await Assert.That(unsupportedSequenceHeader).IsEqualTo(FlvTagConversion.Unsupported);
			await Assert.That(rawAfterUnsupportedSequenceHeader).IsEqualTo(FlvTagConversion.Unsupported);
		}

		static FlvTagConversion Classify(AacAdtsConverter converter, byte[] data)
		{
			return converter.Convert(data, new ArrayBufferWriter<byte>());
		}
	}

	[Test]
	[Arguments(@"0A08", 0, 4, 1)]
	[Arguments(@"13B8", 1, 7, 7)]
	// HE-AAC 显式信令：AOT 5、核心采样率索引 6、2 声道、扩展采样率索引 3、核心 AOT 2
	[Arguments(@"2B1188", 1, 6, 2)]
	// HE-AACv2：AOT 29
	[Arguments(@"EB1188", 1, 6, 2)]
	public async Task ParseAudioSpecificConfigMapsToAdtsFieldsAsync(string hex, int profile, int sampleRateIndex, int channelConfiguration)
	{
		AacAdtsConverter.AdtsConfiguration? configuration = AacAdtsConverter.ParseAudioSpecificConfig(Convert.FromHexString(hex));

		await Assert.That(configuration).IsEqualTo(new AacAdtsConverter.AdtsConfiguration(profile, sampleRateIndex, channelConfiguration));
	}

	[Test]
	[Arguments(@"12", "truncated")]
	[Arguments(@"1790", "explicit sample rate")]
	[Arguments(@"1690", "reserved sample rate index 13")]
	[Arguments(@"1240", "channel configuration 8")]
	[Arguments(@"2B11", "HE-AAC without core object type")]
	public async Task ParseAudioSpecificConfigRejectsUnsupportedConfigAsync(string hex, string reason)
	{
		await Assert.That(AacAdtsConverter.ParseAudioSpecificConfig(Convert.FromHexString(hex))).IsNull().Because(reason);
	}
}
