using MediaToolkit.Flv;
using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;

namespace MediaToolkit.Tests.Flv;

internal sealed class FlvBuilder
{
	private readonly ArrayBufferWriter<byte> _output = new();
	private uint _previousTagSize;

	public FlvBuilder()
	{
		// 版本 1、有音视频、DataOffset 9
		_output.Write<byte>([.. "FLV"u8, 1, 0x05, 0, 0, 0, FlvHeader.Size]);
	}

	public FlvBuilder Tag(FlvTagType type, uint timestamp, params ReadOnlySpan<byte> data)
	{
		// PreviousTagSize、TagType + DataSize、Timestamp + TimestampExtended，最后 3 字节的 StreamID 为 0
		Span<byte> prefix = stackalloc byte[sizeof(uint) + FlvTagHeader.Size];
		BinaryPrimitives.WriteUInt32BigEndian(prefix, _previousTagSize);
		BinaryPrimitives.WriteUInt32BigEndian(prefix.Slice(4), (uint)type << 24 | (uint)data.Length);
		BinaryPrimitives.WriteUInt32BigEndian(prefix.Slice(8), BitOperations.RotateLeft(timestamp, 8));
		_output.Write(prefix);
		_output.Write(data);
		_previousTagSize = (uint)(FlvTagHeader.Size + data.Length);
		return this;
	}

	public byte[] ToArray()
	{
		Span<byte> buffer = stackalloc byte[sizeof(uint)];
		BinaryPrimitives.WriteUInt32BigEndian(buffer, _previousTagSize);
		_output.Write(buffer);
		return _output.WrittenSpan.ToArray();
	}
}
