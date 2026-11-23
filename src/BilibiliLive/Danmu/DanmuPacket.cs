using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;

namespace BilibiliLive.Danmu;

internal readonly record struct DanmuPacket(ushort ProtocolVersion, DanmuOperation Operation, ReadOnlyMemory<byte> Body)
{
	public const int HeaderLength = 16;

	// The server sends packets of a few KB wrapped in one compression level, anything far beyond is hostile or corrupt
	internal const int MaxPacketLength = 4 * 1024 * 1024;
	internal const int MaxDecompressedLength = 16 * 1024 * 1024;
	internal const int MaxCompressionRatio = 256;
	internal const int MaxCompressionDepth = 3;

	private const ushort ZlibVersion = 2;
	private const ushort BrotliVersion = 3;
	private const int DecompressChunkSize = 16 * 1024;

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

	public static List<DanmuPacket> Decode(ReadOnlyMemory<byte> data)
	{
		List<DanmuPacket> packets = [];
		int budget = MaxDecompressedLength;
		DecodeInto(data, packets, 0, ref budget);
		return packets;
	}

	private static void DecodeInto(ReadOnlyMemory<byte> data, List<DanmuPacket> packets, int depth, ref int budget)
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

			if (packetLength > MaxPacketLength)
			{
				throw new InvalidDataException($"Danmu packet length {packetLength} exceeds {MaxPacketLength} bytes.");
			}

			ReadOnlyMemory<byte> body = data.Slice(headerLength, packetLength - headerLength);
			data = data.Slice(packetLength);

			switch (version)
			{
				case ZlibVersion or BrotliVersion when depth >= MaxCompressionDepth:
				{
					throw new InvalidDataException($"Danmu packets are compressed deeper than {MaxCompressionDepth} levels.");
				}
				case ZlibVersion or BrotliVersion:
				{
					DecodeInto(Decompress(body, version, ref budget), packets, depth + 1, ref budget);
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

	// The budget is shared by every packet of a message, so many small bombs cannot add up either
	private static ReadOnlyMemory<byte> Decompress(ReadOnlyMemory<byte> body, ushort version, ref int budget)
	{
		using ReadOnlyMemoryStream input = new(body);
		using Stream decompressor = version is ZlibVersion ? new ZLibStream(input, CompressionMode.Decompress) : new BrotliStream(input, CompressionMode.Decompress);
		long ratioLimit = (long)body.Length * MaxCompressionRatio;
		ArrayBufferWriter<byte> output = new();

		while (decompressor.Read(output.GetSpan(DecompressChunkSize)) is > 0 and int read)
		{
			output.Advance(read);

			if (output.WrittenCount > budget)
			{
				throw new InvalidDataException($"Decompressed danmu packets exceed {MaxDecompressedLength} bytes.");
			}

			if (output.WrittenCount > ratioLimit)
			{
				throw new InvalidDataException($"Danmu packet exceeds the compression ratio limit of {MaxCompressionRatio}.");
			}
		}

		budget -= output.WrittenCount;
		return output.WrittenMemory;
	}
}
