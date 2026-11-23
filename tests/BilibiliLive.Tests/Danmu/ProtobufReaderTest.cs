using BilibiliLive.Danmu;

namespace BilibiliLive.Tests.Danmu;

public class ProtobufReaderTest
{
	private static List<(int Field, ProtobufWireType WireType)> ReadTags(byte[] data)
	{
		List<(int, ProtobufWireType)> tags = [];
		ProtobufReader reader = new(data);

		while (reader.TryReadTag(out int field, out ProtobufWireType wireType))
		{
			tags.Add((field, wireType));
			reader.Skip(wireType);
		}

		return tags;
	}

	private static ulong ReadVarint(byte[] data)
	{
		ProtobufReader reader = new(data);
		ulong value = reader.ReadVarint();
		return reader.IsAtEnd ? value : throw new InvalidOperationException("Bytes left after the varint.");
	}

	[Test]
	[Arguments(new byte[] { 0x00 }, 0UL)]
	[Arguments(new byte[] { 0x7F }, 127UL)]
	[Arguments(new byte[] { 0x96, 0x01 }, 150UL)]
	[Arguments(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01 }, ulong.MaxValue)]
	// 多余的延续字节仍是合法的编码
	[Arguments(new byte[] { 0x81, 0x00 }, 1UL)]
	public async Task VarintIsDecodedAsync(byte[] data, ulong expected)
	{
		await Assert.That(ReadVarint(data)).IsEqualTo(expected);
	}

	[Test]
	public async Task NegativeInt32IsSignExtendedAsync()
	{
		ProtobufReader reader = new([0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01]);
		int value = reader.ReadInt32();

		await Assert.That(value).IsEqualTo(-1);
	}

	[Test]
	[Arguments(new byte[] { 0x80 })]
	// 十个字节，最后一个承载的不止最高位
	[Arguments(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x02 })]
	// 十一个字节
	[Arguments(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x81, 0x00 })]
	public async Task MalformedVarintThrowsAsync(byte[] data)
	{
		await Assert.That(() => ReadVarint(data)).Throws<InvalidDataException>();
	}

	[Test]
	public async Task FieldsOfEveryWireTypeAreSkippedAsync()
	{
		byte[] data =
		[
			0x08, 0x96, 0x01,
			0x11, 1, 2, 3, 4, 5, 6, 7, 8,
			0x1A, 0x03, (byte)'a', (byte)'b', (byte)'c',
			0x25, 1, 2, 3, 4,
			// 字段 16 的 tag 要两个字节
			0x80, 0x01, 0x05,
			// protobuf 允许的最大字段号
			0xF8, 0xFF, 0xFF, 0xFF, 0x0F, 0x00
		];

		await Assert.That(ReadTags(data)).IsEquivalentTo(
		[
			(1, ProtobufWireType.Varint),
			(2, ProtobufWireType.Fixed64),
			(3, ProtobufWireType.LengthDelimited),
			(4, ProtobufWireType.Fixed32),
			(16, ProtobufWireType.Varint),
			((1 << 29) - 1, ProtobufWireType.Varint)
		], CollectionOrdering.Matching);
	}

	[Test]
	public async Task LengthDelimitedFieldIsReadAsync()
	{
		ProtobufReader reader = new([0x12, 0x06, 0xE4, 0xBD, 0xA0, 0xE5, 0xA5, 0xBD, 0x18, 0x01]);

		bool hasTag = reader.TryReadTag(out int field, out ProtobufWireType wireType);
		string value = reader.ReadString();
		bool hasNext = reader.TryReadTag(out int nextField, out _);
		long next = reader.ReadInt64();
		bool hasMore = reader.TryReadTag(out _, out _);

		using (Assert.Multiple())
		{
			await Assert.That((hasTag, field, wireType)).IsEqualTo((true, 2, ProtobufWireType.LengthDelimited));
			await Assert.That(value).IsEqualTo("你好");
			await Assert.That((hasNext, nextField, next)).IsEqualTo((true, 3, 1L));
			await Assert.That(hasMore).IsFalse();
		}
	}

	[Test]
	// 长度超出末尾
	[Arguments(new byte[] { 0x0A, 0x05, 1, 2 })]
	// 长度放不进 int
	[Arguments(new byte[] { 0x0A, 0xFF, 0xFF, 0xFF, 0xFF, 0x0F })]
	// 被截断的定长字段
	[Arguments(new byte[] { 0x09, 1, 2, 3 })]
	[Arguments(new byte[] { 0x0D, 1, 2 })]
	// group
	[Arguments(new byte[] { 0x0B })]
	[Arguments(new byte[] { 0x0C })]
	// 不存在线类型 6 和 7
	[Arguments(new byte[] { 0x0E })]
	// 字段号 0
	[Arguments(new byte[] { 0x00, 0x01 })]
	// 字段号超出 29 位上限
	[Arguments(new byte[] { 0xF8, 0xFF, 0xFF, 0xFF, 0x7F, 0x00 })]
	// tag 被截断
	[Arguments(new byte[] { 0x80 })]
	public async Task MalformedMessageThrowsAsync(byte[] data)
	{
		await Assert.That(() => ReadTags(data)).Throws<InvalidDataException>();
	}
}
