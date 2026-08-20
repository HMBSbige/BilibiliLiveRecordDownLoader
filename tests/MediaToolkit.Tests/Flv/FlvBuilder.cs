using MediaToolkit.Flv;
using System.Buffers.Binary;

namespace MediaToolkit.Tests.Flv;

internal sealed class FlvBuilder
{
	private readonly MemoryStream _stream = new();
	private uint _previousTagSize;

	public FlvBuilder(FlvHeaderFlags flags = FlvHeaderFlags.Video | FlvHeaderFlags.Audio)
	{
		Span<byte> header = stackalloc byte[FlvHeader.Size];
		new FlvHeader(flags, FlvHeader.Size).WriteTo(header);
		_stream.Write(header);
	}

	public FlvBuilder Tag(FlvTagType type, uint timestamp, params ReadOnlySpan<byte> data)
	{
		Span<byte> buffer = stackalloc byte[sizeof(uint) + FlvTagHeader.Size];
		BinaryPrimitives.WriteUInt32BigEndian(buffer, _previousTagSize);
		new FlvTagHeader(type, data.Length, timestamp).WriteTo(buffer.Slice(sizeof(uint)));
		_stream.Write(buffer);
		_stream.Write(data);
		_previousTagSize = (uint)(FlvTagHeader.Size + data.Length);
		return this;
	}

	public byte[] ToArray()
	{
		Span<byte> buffer = stackalloc byte[sizeof(uint)];
		BinaryPrimitives.WriteUInt32BigEndian(buffer, _previousTagSize);
		_stream.Write(buffer);
		return _stream.ToArray();
	}
}
