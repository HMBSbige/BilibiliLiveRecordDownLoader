using System.Buffers;
using System.Buffers.Binary;

namespace MediaToolkit.Flv;

// FLV 里的 H.264 是 AVCC 格式：SPS/PPS 放在 AVCDecoderConfigurationRecord 中，NALU 前缀是长度而不是起始码
internal sealed class AvcAnnexBConverter : IFlvTagConverter
{
	private const int CodecIdAvc = 7;
	private const byte PacketTypeSequenceHeader = 0;
	private const byte PacketTypeNalu = 1;

	// FrameType + CodecID、AVCPacketType、CompositionTime（3 字节）
	private const int VideoTagHeaderSize = 5;

	private static ReadOnlySpan<byte> StartCode => [0, 0, 0, 1];

	private int _nalLengthSize = 4;

	public void Convert(ReadOnlySpan<byte> data, IBufferWriter<byte> output)
	{
		// FrameType 5 是视频信息/命令帧，后面不是 AVC 包
		if (data is not [var flags, var packetType, _, _, _, ..] || (flags & 0x0F) is not CodecIdAvc || flags >> 4 is not (>= 1 and <= 4))
		{
			return;
		}

		ReadOnlySpan<byte> body = data.Slice(VideoTagHeaderSize);

		switch (packetType)
		{
			case PacketTypeSequenceHeader:
			{
				WriteDecoderConfiguration(body, output);
				break;
			}
			case PacketTypeNalu:
			{
				WriteNalUnits(body, output);
				break;
			}
		}
	}

	private void WriteDecoderConfiguration(ReadOnlySpan<byte> record, IBufferWriter<byte> output)
	{
		const int spsOffset = 6;

		if (record is not [1, _, _, _, var lengthSizeMinusOne, var spsCount, ..])
		{
			return;
		}

		_nalLengthSize = (lengthSizeMinusOne & 0x03) + 1;

		ReadOnlySpan<byte> remaining = record.Slice(spsOffset);

		if (!TryWriteParameterSets(ref remaining, spsCount & 0x1F, output) || remaining.IsEmpty)
		{
			return;
		}

		int ppsCount = remaining[0];
		remaining = remaining.Slice(1);
		TryWriteParameterSets(ref remaining, ppsCount, output);
	}

	private static bool TryWriteParameterSets(ref ReadOnlySpan<byte> buffer, int count, IBufferWriter<byte> output)
	{
		for (int i = 0; i < count; ++i)
		{
			if (buffer.Length < sizeof(ushort))
			{
				return false;
			}

			int length = BinaryPrimitives.ReadUInt16BigEndian(buffer);
			buffer = buffer.Slice(sizeof(ushort));

			if (length > buffer.Length)
			{
				return false;
			}

			WriteNalUnit(buffer.Slice(0, length), output);
			buffer = buffer.Slice(length);
		}

		return true;
	}

	private void WriteNalUnits(ReadOnlySpan<byte> buffer, IBufferWriter<byte> output)
	{
		while (buffer.Length >= _nalLengthSize)
		{
			uint length = ReadLength(buffer.Slice(0, _nalLengthSize));
			buffer = buffer.Slice(_nalLengthSize);

			if (length > (uint)buffer.Length)
			{
				return;
			}

			WriteNalUnit(buffer.Slice(0, (int)length), output);
			buffer = buffer.Slice((int)length);
		}
	}

	private static uint ReadLength(ReadOnlySpan<byte> buffer)
	{
		uint length = 0;

		foreach (byte b in buffer)
		{
			length = length << 8 | b;
		}

		return length;
	}

	private static void WriteNalUnit(ReadOnlySpan<byte> nalUnit, IBufferWriter<byte> output)
	{
		if (nalUnit.IsEmpty)
		{
			return;
		}

		int size = StartCode.Length + nalUnit.Length;
		Span<byte> destination = output.GetSpan(size);
		StartCode.CopyTo(destination);
		nalUnit.CopyTo(destination.Slice(StartCode.Length));
		output.Advance(size);
	}
}
