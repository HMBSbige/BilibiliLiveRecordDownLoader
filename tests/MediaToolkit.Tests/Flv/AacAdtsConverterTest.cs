using MediaToolkit.Flv;
using System.Buffers;

namespace MediaToolkit.Tests.Flv;

public class AacAdtsConverterTest
{
	private static byte[] Run(AacAdtsConverter converter, byte[] data)
	{
		ArrayBufferWriter<byte> output = new();
		converter.Convert(data, output);
		return output.WrittenSpan.ToArray();
	}

	[Test]
	public async Task RawFrameIsPrefixedWithAdtsHeaderAsync()
	{
		AacAdtsConverter converter = new();

		byte[] sequenceHeaderOutput = Run(converter, FlvSamples.AacSequenceHeader);
		byte[] frameOutput = Run(converter, FlvSamples.AacFrame);

		byte[] expected = [.. FlvSamples.AdtsHeaderForAacFrame, .. FlvSamples.AacFrame.AsSpan(2)];
		await Assert.That(sequenceHeaderOutput).IsEmpty();
		await Assert.That(Convert.ToHexString(frameOutput)).IsEqualTo(Convert.ToHexString(expected));
	}

	[Test]
	public async Task RawFrameBeforeSequenceHeaderIsDroppedAsync()
	{
		await Assert.That(Run(new AacAdtsConverter(), FlvSamples.AacFrame)).IsEmpty();
	}

	[Test]
	public async Task UnsupportedSequenceHeaderDisablesOutputAsync()
	{
		AacAdtsConverter converter = new();
		Run(converter, FlvSamples.AacSequenceHeader);

		// AudioObjectType 6（BSAC）无法用 ADTS 的 2 位 profile 表示
		Run(converter, Convert.FromHexString(@"AF00" + @"3210"));

		await Assert.That(Run(converter, FlvSamples.AacFrame)).IsEmpty();
	}

	[Test]
	public async Task NonAacAudioProducesNoOutputAsync()
	{
		AacAdtsConverter converter = new();
		Run(converter, FlvSamples.AacSequenceHeader);

		// SoundFormat 2：MP3
		await Assert.That(Run(converter, Convert.FromHexString(@"2F01" + @"21004990"))).IsEmpty();
	}

	[Test]
	[Arguments(@"1210", 1, 4, 2)]
	[Arguments(@"0A08", 0, 4, 1)]
	[Arguments(@"1190", 1, 3, 2)]
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
	[Arguments(@"3210", "object type 6")]
	[Arguments(@"1790", "explicit sample rate")]
	[Arguments(@"1690", "reserved sample rate index 13")]
	[Arguments(@"1240", "channel configuration 8")]
	[Arguments(@"2B11", "HE-AAC without core object type")]
	public async Task ParseAudioSpecificConfigRejectsUnsupportedConfigAsync(string hex, string reason)
	{
		await Assert.That(AacAdtsConverter.ParseAudioSpecificConfig(Convert.FromHexString(hex))).IsNull().Because(reason);
	}
}
