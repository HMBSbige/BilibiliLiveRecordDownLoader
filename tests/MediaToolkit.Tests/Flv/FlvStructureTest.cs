using MediaToolkit.Flv;

namespace MediaToolkit.Tests.Flv;

public class FlvStructureTest
{
	private static readonly byte[] AudioVideoHeader = Convert.FromHexString(@"464C56010500000009");

	// 0x9FC7_0042：低 24 位 C7 00 42 在前，扩展的高 8 位 9F 在后
	private static readonly byte[] VideoTagHeader = Convert.FromHexString(@"09FFFFFF" + @"C700429F" + @"000000");

	[Test]
	public async Task HeaderWriteToProducesSignatureVersionFlagsAndOffsetAsync()
	{
		byte[] buffer = new byte[FlvHeader.Size];
		new FlvHeader(FlvHeaderFlags.Video | FlvHeaderFlags.Audio, FlvHeader.Size).WriteTo(buffer);

		await Assert.That(Convert.ToHexString(buffer)).IsEqualTo(Convert.ToHexString(AudioVideoHeader));
	}

	[Test]
	public async Task HeaderTryReadParsesFlagsAndOffsetAsync()
	{
		bool success = FlvHeader.TryRead(AudioVideoHeader, out FlvHeader header);

		await Assert.That(success).IsTrue();
		await Assert.That(header).IsEqualTo(new FlvHeader(FlvHeaderFlags.Video | FlvHeaderFlags.Audio, 9));
	}

	[Test]
	[Arguments((byte)0b0000_0001, (byte)0b0001)]
	[Arguments((byte)0b0000_0100, (byte)0b0100)]
	[Arguments((byte)0b1111_1011, (byte)0b0001)]
	[Arguments((byte)0b1111_1110, (byte)0b0100)]
	[Arguments((byte)0b1111_1111, (byte)0b0101)]
	[Arguments((byte)0b0000_0000, (byte)0b0000)]
	public async Task HeaderTryReadIgnoresReservedFlagBitsAsync(byte flags, byte expected)
	{
		byte[] buffer = [.. AudioVideoHeader];
		buffer[4] = flags;

		FlvHeader.TryRead(buffer, out FlvHeader header);

		await Assert.That(header.Flags).IsEqualTo((FlvHeaderFlags)expected);
	}

	[Test]
	[Arguments(@"464C56010500000009", 9u)]
	[Arguments(@"464C5601050000000D", 13u)]
	public async Task HeaderTryReadAcceptsValidOffsetAsync(string hex, uint expectedOffset)
	{
		bool success = FlvHeader.TryRead(Convert.FromHexString(hex), out FlvHeader header);

		await Assert.That(success).IsTrue();
		await Assert.That(header.DataOffset).IsEqualTo(expectedOffset);
	}

	[Test]
	[Arguments(@"464C560105000000", "truncated")]
	[Arguments(@"464C580105000000", "wrong signature")]
	[Arguments(@"464C56020500000009", "unsupported version")]
	[Arguments(@"464C56010500000008", "offset inside the header")]
	public async Task HeaderTryReadRejectsInvalidInputAsync(string hex, string reason)
	{
		await Assert.That(FlvHeader.TryRead(Convert.FromHexString(hex), out _)).IsFalse().Because(reason);
	}

	[Test]
	public async Task TagHeaderWriteToUsesExtendedTimestampLayoutAsync()
	{
		byte[] buffer = new byte[FlvTagHeader.Size];
		buffer.AsSpan().Fill(0xAA);

		new FlvTagHeader(FlvTagType.Video, FlvTagHeader.MaxDataSize, 0x9FC7_0042).WriteTo(buffer);

		await Assert.That(Convert.ToHexString(buffer)).IsEqualTo(Convert.ToHexString(VideoTagHeader));
	}

	[Test]
	public async Task TagHeaderReadParsesTypeSizeAndTimestampAsync()
	{
		FlvTagHeader header = FlvTagHeader.Read(VideoTagHeader);

		await Assert.That(header).IsEqualTo(new FlvTagHeader(FlvTagType.Video, FlvTagHeader.MaxDataSize, 0x9FC7_0042));
	}

	[Test]
	[Arguments((byte)8, 0, 0u)]
	[Arguments((byte)8, 418, 0x00FF_FFFFu)]
	[Arguments((byte)9, 0x12_3456, 0x0100_0000u)]
	public async Task TagHeaderRoundTripsAsync(byte type, int dataSize, uint timestamp)
	{
		FlvTagHeader header = new((FlvTagType)type, dataSize, timestamp);
		byte[] buffer = new byte[FlvTagHeader.Size];

		header.WriteTo(buffer);

		await Assert.That(FlvTagHeader.Read(buffer)).IsEqualTo(header);
	}

	[Test]
	public async Task TagHeaderWriteToRejectsOversizedDataAsync()
	{
		FlvTagHeader header = new(FlvTagType.Video, FlvTagHeader.MaxDataSize + 1, 0);

		await Assert.That(() => header.WriteTo(new byte[FlvTagHeader.Size])).Throws<ArgumentOutOfRangeException>();
	}
}
