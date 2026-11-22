using System.Buffers.Binary;

namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

internal static class FlvMetadata
{
	private const byte NumberMarker = 0;
	private const byte BooleanMarker = 1;
	private const byte StringMarker = 2;
	private const byte ObjectMarker = 3;
	private const byte NullMarker = 5;
	private const byte UndefinedMarker = 6;
	private const byte ReferenceMarker = 7;
	private const byte EcmaArrayMarker = 8;
	private const byte ObjectEndMarker = 9;
	private const byte StrictArrayMarker = 10;
	private const byte DateMarker = 11;
	private const byte LongStringMarker = 12;
	private const int MaxDepth = 8;

	// body 从 AMF0 字符串 "onMetaData" 开始
	public static VideoResolution? ReadResolution(ReadOnlySpan<byte> body)
	{
		if (body.Length < 3)
		{
			return null;
		}

		int offset = 3 + BinaryPrimitives.ReadUInt16BigEndian(body.Slice(1));

		if (offset >= body.Length)
		{
			return null;
		}

		switch (body[offset++])
		{
			case EcmaArrayMarker when offset + 4 <= body.Length:
				offset += 4;
				break;
			case ObjectMarker:
				break;
			default:
				return null;
		}

		double? width = null;
		double? height = null;

		while (offset + 2 <= body.Length)
		{
			int keyLength = BinaryPrimitives.ReadUInt16BigEndian(body.Slice(offset));
			offset += 2;

			if (keyLength is 0 || offset + keyLength >= body.Length)
			{
				break;
			}

			ReadOnlySpan<byte> key = body.Slice(offset, keyLength);
			offset += keyLength;

			if (body[offset] is NumberMarker && offset + 9 <= body.Length)
			{
				double value = BinaryPrimitives.ReadDoubleBigEndian(body.Slice(offset + 1));

				if (key.SequenceEqual("width"u8))
				{
					width = value;
				}
				else if (key.SequenceEqual("height"u8))
				{
					height = value;
				}
			}

			if (!TrySkipValue(body, ref offset, 0))
			{
				break;
			}
		}

		return width is { } w && height is { } h && double.IsFinite(w) && double.IsFinite(h) ? VideoResolution.Create((long)w, (long)h) : null;
	}

	private static bool TrySkipValue(ReadOnlySpan<byte> body, ref int offset, int depth)
	{
		if (offset >= body.Length || depth > MaxDepth)
		{
			return false;
		}

		byte marker = body[offset++];

		switch (marker)
		{
			case NumberMarker:
				return Advance(body, ref offset, 8);
			case BooleanMarker:
				return Advance(body, ref offset, 1);
			case StringMarker:
				return offset + 2 <= body.Length && Advance(body, ref offset, 2 + BinaryPrimitives.ReadUInt16BigEndian(body.Slice(offset)));
			case LongStringMarker:
				return offset + 4 <= body.Length && Advance(body, ref offset, 4L + BinaryPrimitives.ReadUInt32BigEndian(body.Slice(offset)));
			case NullMarker or UndefinedMarker:
				return true;
			case ReferenceMarker:
				return Advance(body, ref offset, 2);
			case DateMarker:
				return Advance(body, ref offset, 10);
			case ObjectMarker:
				return TrySkipProperties(body, ref offset, depth);
			case EcmaArrayMarker:
				return Advance(body, ref offset, 4) && TrySkipProperties(body, ref offset, depth);
			case StrictArrayMarker:
			{
				if (offset + 4 > body.Length)
				{
					return false;
				}

				uint count = BinaryPrimitives.ReadUInt32BigEndian(body.Slice(offset));
				offset += 4;

				for (uint i = 0; i < count; ++i)
				{
					if (!TrySkipValue(body, ref offset, depth + 1))
					{
						return false;
					}
				}

				return true;
			}
			default:
				return false;
		}
	}

	private static bool TrySkipProperties(ReadOnlySpan<byte> body, ref int offset, int depth)
	{
		while (offset + 2 <= body.Length)
		{
			int keyLength = BinaryPrimitives.ReadUInt16BigEndian(body.Slice(offset));
			offset += 2;

			if (keyLength is 0)
			{
				return offset < body.Length && body[offset++] is ObjectEndMarker;
			}

			if (!Advance(body, ref offset, keyLength) || !TrySkipValue(body, ref offset, depth + 1))
			{
				return false;
			}
		}

		return false;
	}

	private static bool Advance(ReadOnlySpan<byte> body, ref int offset, long count)
	{
		if (offset + count > body.Length)
		{
			return false;
		}

		offset += (int)count;
		return true;
	}
}
