namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// AV1 的样本是一串自带大小的 OBU，填充是类型 15 的 OBU；对不上和全是填充时的处理同 FillerNalUnits
internal static class FillerObus
{
	private const int PaddingType = 15;
	private const int MaxSizeFieldShift = 7 * 8;

	public static int Measure(ReadOnlySpan<byte> sample)
	{
		int kept = 0;

		for (int offset = 0; offset < sample.Length;)
		{
			if (!TryRead(sample.Slice(offset), out int unitLength, out bool isPadding))
			{
				return -1;
			}

			if (!isPadding)
			{
				kept += unitLength;
			}

			offset += unitLength;
		}

		return kept > 0 ? kept : sample.Length;
	}

	// keptLength 是 Measure 的结果；destination 不能在 source 之后，否则写出会覆盖还没读的数据
	public static void Move(Span<byte> buffer, int source, int length, int destination, int keptLength)
	{
		if (keptLength == length)
		{
			buffer.Slice(source, length).CopyTo(buffer.Slice(destination));
			return;
		}

		for (int offset = source, end = source + length; offset < end;)
		{
			// 不带大小字段的 OBU 到样本末尾为止，不能读到样本外面
			TryRead(buffer.Slice(offset, end - offset), out int unitLength, out bool isPadding);
			Span<byte> unit = buffer.Slice(offset, unitLength);

			if (!isPadding)
			{
				unit.CopyTo(buffer.Slice(destination));
				destination += unitLength;
			}

			offset += unitLength;
		}
	}

	// OBU 头 1 字节，带扩展时多 1 字节；之后是 leb128 编码的大小，最多 8 字节。只有样本里最后一个 OBU 可以不带大小，一直到样本末尾
	private static bool TryRead(ReadOnlySpan<byte> data, out int length, out bool isPadding)
	{
		length = 0;
		isPadding = false;
		byte header = data[0];
		int position = (header & 0x04) is 0 ? 1 : 2;

		// 最高位是必须为 0 的禁止位
		if ((header & 0x80) is not 0 || data.Length < position)
		{
			return false;
		}

		isPadding = (header >> 3 & 0x0F) is PaddingType;

		if ((header & 0x02) is 0)
		{
			length = data.Length;
			return true;
		}

		ulong size = 0;

		for (int shift = 0; ; shift += 7)
		{
			if (shift is MaxSizeFieldShift || position == data.Length)
			{
				return false;
			}

			byte value = data[position++];
			size |= (ulong)(value & 0x7F) << shift;

			if ((value & 0x80) is 0)
			{
				break;
			}
		}

		if (size > (ulong)(data.Length - position))
		{
			return false;
		}

		length = position + (int)size;
		return true;
	}
}
