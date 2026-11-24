using System.Buffers.Binary;

namespace BilibiliLive.Danmu;

internal readonly record struct DanmuPacket(ushort ProtocolVersion, DanmuOperation Operation, ReadOnlyMemory<byte> Body)
{
	public const int HeaderLength = 16;

	internal const ushort ZlibVersion = 2;
	internal const ushort BrotliVersion = 3;

	public static byte[] Encode(DanmuOperation operation, ushort protocolVersion, ReadOnlySpan<byte> body)
	{
		byte[] packet = new byte[HeaderLength + body.Length];
		Span<byte> header = packet;

		BinaryPrimitives.WriteInt32BigEndian(header, packet.Length);
		BinaryPrimitives.WriteUInt16BigEndian(header.Slice(4), HeaderLength);
		BinaryPrimitives.WriteUInt16BigEndian(header.Slice(6), protocolVersion);
		BinaryPrimitives.WriteInt32BigEndian(header.Slice(8), (int)operation);
		BinaryPrimitives.WriteInt32BigEndian(header.Slice(12), 1);
		body.CopyTo(header.Slice(HeaderLength));

		return packet;
	}
}
