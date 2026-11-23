using System.Buffers.Binary;
using System.Numerics;

namespace MediaToolkit.Flv;

// 文件中 Timestamp 的低 24 位在前，高 8 位（TimestampExtended）在后
internal readonly record struct FlvTagHeader(FlvTagType Type, int DataSize, uint Timestamp)
{
	public const int Size = 11;

	public static FlvTagHeader Read(ReadOnlySpan<byte> buffer)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(buffer.Length, Size, nameof(buffer));

		uint typeAndSize = BinaryPrimitives.ReadUInt32BigEndian(buffer);
		uint timestamp = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(4));

		return new FlvTagHeader((FlvTagType)(typeAndSize >> 24), (int)(typeAndSize & 0xFF_FFFF), BitOperations.RotateRight(timestamp, 8));
	}
}
