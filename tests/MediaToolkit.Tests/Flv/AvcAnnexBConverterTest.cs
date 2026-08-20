using MediaToolkit.Flv;
using System.Buffers;

namespace MediaToolkit.Tests.Flv;

public class AvcAnnexBConverterTest
{
	private static byte[] Run(AvcAnnexBConverter converter, byte[] data)
	{
		ArrayBufferWriter<byte> output = new();
		converter.Convert(data, output);
		return output.WrittenSpan.ToArray();
	}

	[Test]
	public async Task SequenceHeaderWritesSpsAndPpsWithStartCodesAsync()
	{
		byte[] output = Run(new AvcAnnexBConverter(), FlvSamples.AvcSequenceHeader);

		await Assert.That(Convert.ToHexString(output)).IsEqualTo(Convert.ToHexString(FlvSamples.AnnexB(FlvSamples.Sps, FlvSamples.Pps)));
	}

	[Test]
	public async Task NaluPacketReplacesLengthPrefixesWithStartCodesAsync()
	{
		AvcAnnexBConverter converter = new();
		Run(converter, FlvSamples.AvcSequenceHeader);

		byte[] output = Run(converter, FlvSamples.AvcKeyFrame);

		await Assert.That(Convert.ToHexString(output)).IsEqualTo(Convert.ToHexString(FlvSamples.AnnexB(FlvSamples.IdrSlice, FlvSamples.Sei)));
	}

	[Test]
	public async Task NaluPacketUsesLengthSizeFromSequenceHeaderAsync()
	{
		AvcAnnexBConverter converter = new();
		// lengthSizeMinusOne = 1，没有 SPS/PPS
		Run(converter, Convert.FromHexString(@"1700000000" + @"0164001FFD" + @"E0" + @"00"));

		byte[] output = Run(converter, Convert.FromHexString(@"2701000000" + @"0005" + @"6588840033" + @"0003" + @"060501"));

		await Assert.That(Convert.ToHexString(output)).IsEqualTo(Convert.ToHexString(FlvSamples.AnnexB(FlvSamples.IdrSlice, FlvSamples.Sei)));
	}

	[Test]
	public async Task NaluPacketStopsAtTruncatedNalUnitAsync()
	{
		// 第二个 NALU 声明 9 字节，实际只剩 3 字节
		byte[] output = Run(new AvcAnnexBConverter(), Convert.FromHexString(@"2701000000" + @"00000005" + @"6588840033" + @"00000009" + @"060501"));

		await Assert.That(Convert.ToHexString(output)).IsEqualTo(Convert.ToHexString(FlvSamples.AnnexB(FlvSamples.IdrSlice)));
	}

	[Test]
	[Arguments(@"1C01000000000000056588840033", "HEVC codec id")]
	[Arguments(@"5701000000000000056588840033", "video info/command frame")]
	[Arguments(@"9701000000000000056588840033", "enhanced RTMP header")]
	[Arguments(@"1702000000", "end of sequence")]
	[Arguments(@"17010000", "truncated video tag header")]
	public async Task UnsupportedPacketsProduceNoOutputAsync(string hex, string reason)
	{
		byte[] output = Run(new AvcAnnexBConverter(), Convert.FromHexString(hex));

		await Assert.That(output).IsEmpty().Because(reason);
	}
}
