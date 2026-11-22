namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// 编码器以固定码率输出时插入填充数据 NAL（H.264 类型 12，HEVC 类型 38）凑码率，里面没有画面，解码器直接丢弃
internal static class FillerNalUnits
{
	private const int AvcFillerType = 12;
	private const int HevcFillerType = 38;

	// 解码器配置记录里 NAL 长度字段的宽度：AVCDecoderConfigurationRecord 第 5 字节、HEVCDecoderConfigurationRecord 第 22 字节的低 2 位加 1；记录不完整时返回 0
	public static int ReadLengthSize(ReadOnlySpan<byte> record, NalSyntax syntax)
	{
		return syntax switch
		{
			NalSyntax.Avc when record.Length >= 5 => (record[4] & 0x03) + 1,
			NalSyntax.Hevc when record.Length >= 23 => (record[21] & 0x03) + 1,
			_ => 0
		};
	}

	// 样本由带长度前缀的 NAL 组成。返回去掉填充之后的长度；长度加起来不等于样本长度时返回 -1。全是填充的样本去掉之后就不是合法的样本了，保留原样
	public static int Measure(ReadOnlySpan<byte> sample, int lengthSize, NalSyntax syntax)
	{
		int kept = 0;
		bool hasKept = false;

		for (int offset = 0; offset < sample.Length;)
		{
			if (sample.Length - offset < lengthSize)
			{
				return -1;
			}

			long unitLength = lengthSize + ReadLength(sample.Slice(offset), lengthSize);

			if (unitLength > sample.Length - offset)
			{
				return -1;
			}

			if (!IsFiller(sample.Slice(offset, (int)unitLength), lengthSize, syntax))
			{
				kept += (int)unitLength;
				hasKept = true;
			}

			offset += (int)unitLength;
		}

		return hasKept ? kept : sample.Length;
	}

	// 把 buffer 里 source 处的样本去掉填充后移到 destination，keptLength 是 Measure 的结果。destination 不在 source 之后，前面已经写出的数据不会被后面的读取覆盖
	public static void Move(Span<byte> buffer, int source, int length, int destination, int keptLength, int lengthSize, NalSyntax syntax)
	{
		if (keptLength == length)
		{
			buffer.Slice(source, length).CopyTo(buffer.Slice(destination));
			return;
		}

		for (int offset = source, end = source + length; offset < end;)
		{
			int unitLength = lengthSize + (int)ReadLength(buffer.Slice(offset), lengthSize);
			Span<byte> unit = buffer.Slice(offset, unitLength);

			if (!IsFiller(unit, lengthSize, syntax))
			{
				unit.CopyTo(buffer.Slice(destination));
				destination += unitLength;
			}

			offset += unitLength;
		}
	}

	// 长度为 0 的 NAL 没有类型，当作普通数据留着
	private static bool IsFiller(ReadOnlySpan<byte> unit, int lengthSize, NalSyntax syntax)
	{
		if (unit.Length <= lengthSize)
		{
			return false;
		}

		byte header = unit[lengthSize];

		return syntax is NalSyntax.Avc ? (header & 0x1F) is AvcFillerType : (header >> 1 & 0x3F) is HevcFillerType;
	}

	private static uint ReadLength(ReadOnlySpan<byte> data, int lengthSize)
	{
		uint length = 0;

		foreach (byte value in data.Slice(0, lengthSize))
		{
			length = length << 8 | value;
		}

		return length;
	}
}

// H.264 和 HEVC 的 NAL 头不一样，类型的位置也不同
internal enum NalSyntax
{
	Avc,
	Hevc
}
