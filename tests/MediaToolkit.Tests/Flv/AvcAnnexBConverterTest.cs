using MediaToolkit.Flv;
using System.Buffers;

namespace MediaToolkit.Tests.Flv;

public class AvcAnnexBConverterTest
{
	private static FlvTagConversion Classify(string hex)
	{
		return new AvcAnnexBConverter().Convert(Convert.FromHexString(hex), new ArrayBufferWriter<byte>());
	}

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
		byte[] output = Run(new AvcAnnexBConverter(), Convert.FromHexString(@"2701000000" + @"00000005" + @"6588840033" + @"00000009" + @"060501"));

		await Assert.That(Convert.ToHexString(output)).IsEqualTo(Convert.ToHexString(FlvSamples.AnnexB(FlvSamples.IdrSlice)));
	}

	[Test]
	public async Task NaluPacketWithDataIsAFrameAsync()
	{
		await Assert.That(Classify(@"1701000000" + @"00000005" + @"6588840033")).IsEqualTo(FlvTagConversion.Frame);
	}

	[Test]
	[Arguments(@"1700000000" + @"0164001FFF" + @"E10006" + @"6764001FACD9" + @"010004" + @"68EBE3CB", "sequence header carries parameter sets")]
	[Arguments(@"2701000000" + @"00000009" + @"060501", "NALU length exceeds the tag")]
	[Arguments(@"2701000000" + @"00000000", "only an empty NALU")]
	[Arguments(@"5701000000" + @"00000005" + @"6588840033", "video info/command frame")]
	[Arguments(@"1702000000", "end of sequence")]
	[Arguments(@"17010000", "truncated video tag header")]
	public async Task TagsWithoutPictureDataAreNotFramesAsync(string hex, string reason)
	{
		await Assert.That(Classify(hex)).IsEqualTo(FlvTagConversion.NonFrame).Because(reason);
	}

	[Test]
	[Arguments(@"1C00000000" + @"01", "HEVC codec id")]
	[Arguments(@"9701000000" + @"00000005" + @"6588840033", "enhanced RTMP header")]
	[Arguments(@"1700000000" + @"0064001FFF" + @"E10006" + @"6764001FACD9" + @"010004" + @"68EBE3CB", "unknown configuration version")]
	[Arguments(@"1700000000" + @"0164001FFF" + @"E10006" + @"6764001FACD9", "sequence header ends before the PPS count")]
	[Arguments(@"1703000000", "unknown AVC packet type")]
	public async Task UnconvertibleTagsAreUnsupportedAsync(string hex, string reason)
	{
		await Assert.That(Classify(hex)).IsEqualTo(FlvTagConversion.Unsupported).Because(reason);
	}
}
