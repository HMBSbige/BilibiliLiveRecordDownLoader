using System.Buffers.Binary;

namespace MediaToolkit.Flv;

internal readonly record struct FlvHeader(FlvHeaderFlags Flags, uint DataOffset)
{
	public const int Size = 9;

	private const byte Version = 1;

	private static ReadOnlySpan<byte> Signature => "FLV"u8;

	public static bool TryRead(ReadOnlySpan<byte> buffer, out FlvHeader header)
	{
		header = default;

		if (buffer.Length < Size || !buffer.Slice(0, Signature.Length).SequenceEqual(Signature) || buffer[3] is not Version)
		{
			return false;
		}

		uint dataOffset = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(5));

		if (dataOffset < Size)
		{
			return false;
		}

		header = new FlvHeader((FlvHeaderFlags)buffer[4] & (FlvHeaderFlags.Video | FlvHeaderFlags.Audio), dataOffset);
		return true;
	}

	public void WriteTo(Span<byte> buffer)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(buffer.Length, Size, nameof(buffer));

		Signature.CopyTo(buffer);
		buffer[3] = Version;
		buffer[4] = (byte)Flags;
		BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(5), DataOffset);
	}
}
