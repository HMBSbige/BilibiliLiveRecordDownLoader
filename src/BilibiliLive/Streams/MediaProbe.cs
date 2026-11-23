using System.Buffers.Binary;

namespace BilibiliLive.Streams;

internal static class MediaProbe
{
	internal const int TransportPacketSize = 188;
	internal const int TransportPacketsToCheck = 3;

	private const int BoxHeaderSize = 8;
	private const int LargeBoxHeaderSize = 16;

	public static ReadOnlySpan<byte> FlvSignature => "FLV"u8;

	public static bool IsFlv(ReadOnlySpan<byte> data)
	{
		return data.StartsWith(FlvSignature);
	}

	// 任何数据里都常见单独的 0x47，所以要连续几个包都对齐
	public static bool IsTransportStream(ReadOnlySpan<byte> data)
	{
		for (int offset = 0; offset < TransportPacketSize && offset + TransportPacketSize * TransportPacketsToCheck <= data.Length; ++offset)
		{
			if (IsSynchronized(data.Slice(offset)))
			{
				return true;
			}
		}

		return false;

		static bool IsSynchronized(ReadOnlySpan<byte> data)
		{
			for (int packet = 0; packet < TransportPacketsToCheck; ++packet)
			{
				if (data[packet * TransportPacketSize] is not 0x47)
				{
					return false;
				}
			}

			return true;
		}
	}

	public static bool IsIsoBmff(ReadOnlySpan<byte> data, bool initialization)
	{
		ReadOnlySpan<byte> requiredBox = initialization ? "moov"u8 : "moof"u8;
		bool hasRequiredBox = false;

		while (!data.IsEmpty)
		{
			if (data.Length < BoxHeaderSize)
			{
				return false;
			}

			ulong size = BinaryPrimitives.ReadUInt32BigEndian(data);
			ReadOnlySpan<byte> type = data.Slice(4, 4);
			int headerSize = BoxHeaderSize;

			if (size is 1)
			{
				if (data.Length < LargeBoxHeaderSize)
				{
					return false;
				}

				size = BinaryPrimitives.ReadUInt64BigEndian(data.Slice(BoxHeaderSize));
				headerSize = LargeBoxHeaderSize;
			}
			else if (size is 0)
			{
				size = (ulong)data.Length;
			}

			if (size < (ulong)headerSize || size > (ulong)data.Length || type.ContainsAnyExceptInRange((byte)0x20, (byte)0x7E))
			{
				return false;
			}

			hasRequiredBox |= type.SequenceEqual(requiredBox);
			data = data.Slice((int)size);
		}

		return hasRequiredBox;
	}
}
