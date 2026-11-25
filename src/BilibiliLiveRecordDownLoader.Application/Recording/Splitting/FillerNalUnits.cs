namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

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

	// 只用于 Measure 判定有填充可删的样本；destination 不能在 source 之后，否则写出会覆盖还没读的数据
	public static void Move(Span<byte> buffer, int source, int length, int destination, int lengthSize, NalSyntax syntax)
	{
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
