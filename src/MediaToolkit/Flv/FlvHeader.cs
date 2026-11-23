using System.Buffers.Binary;

namespace MediaToolkit.Flv;

internal static class FlvHeader
{
	public const int Size = 9;

	private const byte Version = 1;

	// 头里的音视频标志不可靠，有没有这条流以实际转换出的帧为准
	public static uint? ReadDataOffset(ReadOnlySpan<byte> buffer)
	{
		if (buffer.Length < Size || buffer is not [(byte)'F', (byte)'L', (byte)'V', Version, ..])
		{
			return null;
		}

		uint dataOffset = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(5));
		return dataOffset >= Size ? dataOffset : null;
	}
}
