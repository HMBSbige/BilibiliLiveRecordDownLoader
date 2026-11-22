using System.Text;

namespace BilibiliLive.Danmu;

internal enum ProtobufWireType
{
	Varint = 0,
	Fixed64 = 1,
	LengthDelimited = 2,
	StartGroup = 3,
	EndGroup = 4,
	Fixed32 = 5
}

// Only the few messages the danmu server sends as protobuf are read, a schema-driven library would be a dependency for a handful of fields
internal ref struct ProtobufReader(ReadOnlySpan<byte> data)
{
	private const int MaxVarintLength = 10;
	private const ulong MaxFieldNumber = (1 << 29) - 1;

	private readonly ReadOnlySpan<byte> _data = data;
	private int _position;

	public readonly bool IsAtEnd => _position >= _data.Length;

	public bool TryReadTag(out int fieldNumber, out ProtobufWireType wireType)
	{
		if (IsAtEnd)
		{
			fieldNumber = 0;
			wireType = default;
			return false;
		}

		ulong tag = ReadVarint();
		ulong number = tag >> 3;

		if (number is 0 or > MaxFieldNumber)
		{
			throw new InvalidDataException($"Invalid protobuf field number {number}.");
		}

		fieldNumber = (int)number;
		wireType = (ProtobufWireType)(tag & 7);
		return true;
	}

	public ulong ReadVarint()
	{
		ulong value = 0;

		for (int i = 0; i < MaxVarintLength; ++i)
		{
			if (_position >= _data.Length)
			{
				throw new InvalidDataException("Truncated protobuf varint.");
			}

			byte b = _data[_position++];

			// The tenth byte holds only the top bit of a 64-bit value
			if (i is MaxVarintLength - 1 && b > 1)
			{
				throw new InvalidDataException("Protobuf varint overflows 64 bits.");
			}

			value |= (ulong)(b & 0x7F) << (7 * i);

			if (b < 0x80)
			{
				return value;
			}
		}

		throw new InvalidDataException("Protobuf varint overflows 64 bits.");
	}

	public long ReadInt64()
	{
		return (long)ReadVarint();
	}

	// Negative int32 values are sign-extended to ten bytes, so truncating the 64-bit value restores them
	public int ReadInt32()
	{
		return (int)ReadVarint();
	}

	public ReadOnlySpan<byte> ReadLengthDelimited()
	{
		ulong length = ReadVarint();

		if (length > (ulong)(_data.Length - _position))
		{
			throw new InvalidDataException($"Protobuf field length {length} exceeds the remaining {_data.Length - _position} bytes.");
		}

		ReadOnlySpan<byte> value = _data.Slice(_position, (int)length);
		_position += (int)length;
		return value;
	}

	public string ReadString()
	{
		return Encoding.UTF8.GetString(ReadLengthDelimited());
	}

	public void Skip(ProtobufWireType wireType)
	{
		switch (wireType)
		{
			case ProtobufWireType.Varint:
			{
				ReadVarint();
				break;
			}
			case ProtobufWireType.Fixed64:
			{
				ReadFixed(sizeof(ulong));
				break;
			}
			case ProtobufWireType.LengthDelimited:
			{
				ReadLengthDelimited();
				break;
			}
			case ProtobufWireType.Fixed32:
			{
				ReadFixed(sizeof(uint));
				break;
			}
			default:
			{
				// Groups have been deprecated since proto2 and nothing the server sends uses them
				throw new InvalidDataException($"Unsupported protobuf wire type {(int)wireType}.");
			}
		}
	}

	private ReadOnlySpan<byte> ReadFixed(int length)
	{
		if (length > _data.Length - _position)
		{
			throw new InvalidDataException("Truncated protobuf fixed-width field.");
		}

		ReadOnlySpan<byte> value = _data.Slice(_position, length);
		_position += length;
		return value;
	}
}
