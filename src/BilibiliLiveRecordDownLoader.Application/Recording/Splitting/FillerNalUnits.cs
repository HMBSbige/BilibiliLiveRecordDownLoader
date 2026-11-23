namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// 固定码率编码器为凑码率插入的填充 NAL，里面没有画面，解码器直接丢弃
internal static class FillerNalUnits
{
	private const int AvcFillerType = 12;
	private const int HevcFillerType = 38;

	// 解码器配置记录里的 lengthSizeMinusOne 加 1；记录不完整时返回 0
	public static int ReadLengthSize(ReadOnlySpan<byte> record, NalSyntax syntax)
	{
		return syntax switch
		{
			NalSyntax.Avc when record.Length >= 5 => (record[4] & 0x03) + 1,
			NalSyntax.Hevc when record.Length >= 23 => (record[21] & 0x03) + 1,
			_ => 0
		};
	}

	// 长度前缀加起来不等于样本长度时返回 -1；全是填充的样本去掉之后就不是合法的样本了，保留原样
	public static int Measure(ReadOnlySpan<byte> sample, int lengthSize, NalSyntax syntax)
	{
		int kept = 0;

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
			}

			offset += (int)unitLength;
		}

		return kept > 0 ? kept : sample.Length;
	}

	// 原地去掉填充时原样保留的一段，返回 length
	public static int Keep(Span<byte> buffer, int source, int destination, int length)
	{
		if (source != destination)
		{
			buffer.Slice(source, length).CopyTo(buffer.Slice(destination));
		}

		return length;
	}

	// keptLength 是 Measure 的结果；destination 不能在 source 之后，否则写出会覆盖还没读的数据
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

internal enum NalSyntax
{
	Avc,
	Hevc
}
