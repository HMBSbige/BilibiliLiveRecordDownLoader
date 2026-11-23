using MediaToolkit.Flv;

namespace MediaToolkit.Tests.Flv;

public class FlvStructureTest
{
	[Test]
	[Arguments(@"464C560105000000", "truncated")]
	[Arguments(@"464C580105000000", "wrong signature")]
	[Arguments(@"464C56020500000009", "unsupported version")]
	[Arguments(@"464C56010500000008", "offset inside the header")]
	public async Task HeaderReadRejectsInvalidInputAsync(string hex, string reason)
	{
		await Assert.That(FlvHeader.ReadDataOffset(Convert.FromHexString(hex))).IsNull().Because(reason);
	}

	[Test]
	public async Task TagHeaderReadParsesTypeSizeAndExtendedTimestampAsync()
	{
		// 0x9FC7_0042：低 24 位 C7 00 42 在前，扩展的高 8 位 9F 在后
		byte[] buffer = Convert.FromHexString(@"09FFFFFF" + @"C700429F" + @"000000");

		await Assert.That(FlvTagHeader.Read(buffer)).IsEqualTo(new FlvTagHeader(FlvTagType.Video, 0xFF_FFFF, 0x9FC7_0042));
	}
}
