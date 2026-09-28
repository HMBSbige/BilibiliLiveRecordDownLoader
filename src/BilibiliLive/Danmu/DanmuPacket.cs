using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace BilibiliLive.Danmu;

internal readonly record struct DanmuPacket(ushort ProtocolVersion, DanmuOperation Operation, ReadOnlyMemory<byte> Body)
{
	public const int HeaderLength = 16;

	private const ushort ZlibVersion = 2;
	private const ushort BrotliVersion = 3;

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

	/// <summary>Splits a buffer into packets, expanding compressed packets into the packets they carry.</summary>
	public static List<DanmuPacket> Decode(ReadOnlyMemory<byte> data)
	{
		List<DanmuPacket> packets = [];
		DecodeInto(data, packets);
		return packets;
	}

	private static void DecodeInto(ReadOnlyMemory<byte> data, List<DanmuPacket> packets)
	{
		while (!data.IsEmpty)
		{
			ReadOnlySpan<byte> span = data.Span;

			if (span.Length < HeaderLength)
			{
				throw new InvalidDataException("Truncated danmu packet header.");
			}

			int packetLength = BinaryPrimitives.ReadInt32BigEndian(span);
			ushort headerLength = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(4));
			ushort version = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(6));
			DanmuOperation operation = (DanmuOperation)BinaryPrimitives.ReadInt32BigEndian(span.Slice(8));

			if (headerLength < HeaderLength || packetLength < headerLength || packetLength > span.Length)
			{
				throw new InvalidDataException($"Invalid danmu packet: length {packetLength}, header {headerLength}, available {span.Length}.");
			}

			ReadOnlyMemory<byte> body = data.Slice(headerLength, packetLength - headerLength);
			data = data.Slice(packetLength);

			switch (version)
			{
				case ZlibVersion:
				{
					DecodeInto(Decompress(body, static s => new ZLibStream(s, CompressionMode.Decompress)), packets);
					break;
				}
				case BrotliVersion:
				{
					DecodeInto(Decompress(body, static s => new BrotliStream(s, CompressionMode.Decompress)), packets);
					break;
				}
				default:
				{
					packets.Add(new DanmuPacket(version, operation, body));
					break;
				}
			}
		}
	}

	private static byte[] Decompress(ReadOnlyMemory<byte> body, Func<Stream, Stream> createDecompressor)
	{
		using MemoryStream input = MemoryMarshal.TryGetArray(body, out ArraySegment<byte> segment) && segment.Array is { } array
			? new MemoryStream(array, segment.Offset, segment.Count, false)
			: new MemoryStream(body.ToArray(), false);
		using Stream decompressor = createDecompressor(input);
		using MemoryStream output = new();
		decompressor.CopyTo(output);
		return output.ToArray();
	}
}
