using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;

namespace BilibiliLive.Danmu;

// 解压出的包体借用池里的数组，Decode 返回的包只在下一次 Decode 或 Dispose 之前有效
internal sealed class DanmuPacketDecoder : IDisposable
{
	// 服务器下发的包只有几 KB，只套一层压缩，远超这些的不是恶意就是损坏
	internal const int MaxPacketLength = 4 * 1024 * 1024;
	internal const int MaxDecompressedLength = 16 * 1024 * 1024;
	internal const int MaxCompressionDepth = 3;
	private const int MaxCompressionRatio = 256;
	private const int MinimumBufferLength = 4 * 1024;

	private readonly List<DanmuPacket> _packets = [];
	private readonly List<byte[]> _rented = [];
	private ZLibDecoder? _zlib;

	// 一条消息的所有包共用一份预算，许多小炸弹也累加不起来
	private int _budget;

	public List<DanmuPacket> Decode(ReadOnlyMemory<byte> data)
	{
		ReturnRented();
		_packets.Clear();
		_budget = MaxDecompressedLength;
		DecodeInto(data, 0);
		return _packets;
	}

	public void Dispose()
	{
		ReturnRented();
		_packets.Clear();
		_zlib?.Dispose();
	}

	private void DecodeInto(ReadOnlyMemory<byte> data, int depth)
	{
		while (!data.IsEmpty)
		{
			ReadOnlySpan<byte> span = data.Span;

			if (span.Length < DanmuPacket.HeaderLength)
			{
				throw new InvalidDataException("Truncated danmu packet header.");
			}

			int packetLength = BinaryPrimitives.ReadInt32BigEndian(span);
			ushort headerLength = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(4));
			ushort version = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(6));
			DanmuOperation operation = (DanmuOperation)BinaryPrimitives.ReadInt32BigEndian(span.Slice(8));

			if (headerLength < DanmuPacket.HeaderLength || packetLength < headerLength || packetLength > span.Length)
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
				case DanmuPacket.ZlibVersion or DanmuPacket.BrotliVersion when depth >= MaxCompressionDepth:
				{
					throw new InvalidDataException($"Danmu packets are compressed deeper than {MaxCompressionDepth} levels.");
				}
				case DanmuPacket.ZlibVersion or DanmuPacket.BrotliVersion:
				{
					DecodeInto(Decompress(body.Span, version), depth + 1);
					break;
				}
				default:
				{
					_packets.Add(new DanmuPacket(version, operation, body));
					break;
				}
			}
		}
	}

	private ReadOnlyMemory<byte> Decompress(ReadOnlySpan<byte> source, ushort version)
	{
		long ratioLimit = (long)source.Length * MaxCompressionRatio;
		int limit = (int)Math.Min(_budget, ratioLimit);
		byte[] buffer = ArrayPool<byte>.Shared.Rent((int)Math.Min(Math.Max(source.Length * 4L, MinimumBufferLength), limit + 1L));
		int capacity = (int)Math.Min(buffer.Length, limit + 1L);
		int written = 0;
		BrotliDecoder brotli = default;
		ZLibDecoder? zlib = null;

		if (version is DanmuPacket.ZlibVersion)
		{
			zlib = _zlib ??= new ZLibDecoder();
			zlib.Reset();
		}

		try
		{
			while (true)
			{
				Span<byte> destination = buffer.AsSpan(written, capacity - written);
				OperationStatus status = zlib is not null ? zlib.Decompress(source, destination, out int consumed, out int count) : brotli.Decompress(source, destination, out consumed, out count);
				source = source.Slice(consumed);
				written += count;

				if (written > limit || status is OperationStatus.DestinationTooSmall && capacity > limit)
				{
					throw new InvalidDataException(_budget <= ratioLimit ? $"Decompressed danmu packets exceed {MaxDecompressedLength} bytes." : $"Danmu packet exceeds the compression ratio limit of {MaxCompressionRatio}.");
				}

				switch (status)
				{
					case OperationStatus.InvalidData:
					{
						throw new InvalidDataException("Danmu packet contains invalid compressed data.");
					}
					case OperationStatus.DestinationTooSmall:
					{
						buffer = Grow(buffer, written, (int)Math.Min(capacity * 2L, limit + 1L));
						capacity = (int)Math.Min(buffer.Length, limit + 1L);
						break;
					}
					default:
					{
						_rented.Add(buffer);
						_budget -= written;
						return buffer.AsMemory(0, written);
					}
				}
			}
		}
		catch
		{
			ArrayPool<byte>.Shared.Return(buffer);
			throw;
		}
		finally
		{
			brotli.Dispose();
		}
	}

	private static byte[] Grow(byte[] array, int length, int minimumLength)
	{
		byte[] larger = ArrayPool<byte>.Shared.Rent(minimumLength);
		array.AsSpan(0, length).CopyTo(larger);
		ArrayPool<byte>.Shared.Return(array);
		return larger;
	}

	private void ReturnRented()
	{
		foreach (byte[] array in _rented)
		{
			ArrayPool<byte>.Shared.Return(array);
		}

		_rented.Clear();
	}
}
