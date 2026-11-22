using System.Buffers.Binary;

namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

internal static class ParameterSets
{
	private const int AvcSpsType = 7;
	private const int HevcSpsType = 33;

	// AVCDecoderConfigurationRecord：第 6 字节低 5 位是 SPS 个数，之后每个 SPS 前有 2 字节长度
	public static VideoResolution? ReadAvcConfiguration(ReadOnlySpan<byte> record)
	{
		if (record.Length < 8 || (record[5] & 0x1F) is 0)
		{
			return null;
		}

		int length = BinaryPrimitives.ReadUInt16BigEndian(record.Slice(6));
		return record.Length >= 8 + length ? ReadAvcSps(record.Slice(8, length)) : null;
	}

	// HEVCDecoderConfigurationRecord：22 字节固定部分之后是数组个数和按 NAL 类型分组的数组
	public static VideoResolution? ReadHevcConfiguration(ReadOnlySpan<byte> record)
	{
		if (record.Length < 23)
		{
			return null;
		}

		int offset = 23;

		for (int array = 0; array < record[22]; ++array)
		{
			if (record.Length < offset + 3)
			{
				return null;
			}

			int type = record[offset] & 0x3F;
			int count = BinaryPrimitives.ReadUInt16BigEndian(record.Slice(offset + 1));
			offset += 3;

			for (int i = 0; i < count; ++i)
			{
				if (record.Length < offset + 2)
				{
					return null;
				}

				int length = BinaryPrimitives.ReadUInt16BigEndian(record.Slice(offset));
				offset += 2;

				if (record.Length < offset + length)
				{
					return null;
				}

				if (type is HevcSpsType)
				{
					return ReadHevcSps(record.Slice(offset, length));
				}

				offset += length;
			}
		}

		return null;
	}

	// 找第一个 SPS；它可能在 data 末尾被截断，读过头时返回 null，等更多数据
	public static VideoResolution? ReadAnnexB(ReadOnlySpan<byte> data, NalSyntax syntax)
	{
		int start = FindStartCode(data, 0);

		while (start >= 0)
		{
			int nalStart = start + 3;
			int next = FindStartCode(data, nalStart);
			ReadOnlySpan<byte> nal = next < 0 ? data.Slice(nalStart) : data.Slice(nalStart, next - nalStart);

			if (nal.Length > 0 && ReadSps(nal, syntax) is { IsSps: true } sps)
			{
				return sps.Resolution;
			}

			start = next;
		}

		return null;
	}

	private static (bool IsSps, VideoResolution? Resolution) ReadSps(ReadOnlySpan<byte> nal, NalSyntax syntax)
	{
		return syntax switch
		{
			NalSyntax.Avc => (nal[0] & 0x1F) is AvcSpsType ? (true, ReadAvcSps(nal)) : (false, null),
			NalSyntax.Hevc => (nal[0] >> 1 & 0x3F) is HevcSpsType ? (true, ReadHevcSps(nal)) : (false, null)
		};
	}

	// nal 带 1 字节 NAL 头
	public static VideoResolution? ReadAvcSps(ReadOnlySpan<byte> nal)
	{
		RbspReader reader = new(nal);
		reader.Skip(8);
		uint profile = reader.ReadBits(8);
		reader.Skip(16);
		reader.ReadUe();
		uint chromaFormat = 1;
		bool isSeparateColourPlane = false;

		if (profile is 100 or 110 or 122 or 244 or 44 or 83 or 86 or 118 or 128 or 138 or 139 or 134 or 135)
		{
			chromaFormat = reader.ReadUe();

			if (chromaFormat is 3)
			{
				isSeparateColourPlane = reader.ReadBit() is 1;
			}

			reader.ReadUe();
			reader.ReadUe();
			reader.Skip(1);

			if (reader.ReadBit() is 1)
			{
				for (int i = 0; i < (chromaFormat is 3 ? 12 : 8) && !reader.IsOverrun; ++i)
				{
					if (reader.ReadBit() is 1)
					{
						SkipScalingList(ref reader, i < 6 ? 16 : 64);
					}
				}
			}
		}

		reader.ReadUe();
		uint pictureOrderCountType = reader.ReadUe();

		if (pictureOrderCountType is 0)
		{
			reader.ReadUe();
		}
		else if (pictureOrderCountType is 1)
		{
			reader.Skip(1);
			reader.ReadSe();
			reader.ReadSe();
			uint cycle = reader.ReadUe();

			// 标准限定不超过 255
			if (cycle > 255)
			{
				return null;
			}

			for (uint i = 0; i < cycle; ++i)
			{
				reader.ReadSe();
			}
		}

		reader.ReadUe();
		reader.Skip(1);
		long widthInMacroblocks = reader.ReadUe() + 1L;
		long heightInMapUnits = reader.ReadUe() + 1L;
		int frameHeightFactor = reader.ReadBit() is 1 ? 1 : 2;

		if (frameHeightFactor is 2)
		{
			reader.Skip(1);
		}

		reader.Skip(1);
		(long left, long right, long top, long bottom) = reader.ReadBit() is 1 ? (reader.ReadUe(), reader.ReadUe(), reader.ReadUe(), reader.ReadUe()) : (0L, 0L, 0L, 0L);

		if (reader.IsOverrun)
		{
			return null;
		}

		// 单色或独立色彩平面时裁剪单位是亮度像素，否则按色度采样换算
		(int cropX, int cropY) = isSeparateColourPlane || chromaFormat is 0 ? (1, frameHeightFactor) : (chromaFormat is 3 ? 1 : 2, (chromaFormat is 1 ? 2 : 1) * frameHeightFactor);
		return VideoResolution.Create(widthInMacroblocks * 16 - cropX * (left + right), frameHeightFactor * heightInMapUnits * 16 - cropY * (top + bottom));
	}

	// nal 带 2 字节 NAL 头
	public static VideoResolution? ReadHevcSps(ReadOnlySpan<byte> nal)
	{
		RbspReader reader = new(nal);
		reader.Skip(16 + 4);
		int maxSubLayers = (int)reader.ReadBits(3);
		reader.Skip(1);

		// profile_tier_level 的通用部分固定 96 位，子层部分先是各层的两个存在标志，不足 8 层时补齐到 16 位
		reader.Skip(96);
		Span<bool> profilePresent = stackalloc bool[8];
		Span<bool> levelPresent = stackalloc bool[8];

		for (int i = 0; i < maxSubLayers; ++i)
		{
			profilePresent[i] = reader.ReadBit() is 1;
			levelPresent[i] = reader.ReadBit() is 1;
		}

		if (maxSubLayers > 0)
		{
			reader.Skip(2 * (8 - maxSubLayers));
		}

		for (int i = 0; i < maxSubLayers; ++i)
		{
			reader.Skip((profilePresent[i] ? 88 : 0) + (levelPresent[i] ? 8 : 0));
		}

		reader.ReadUe();
		uint chromaFormat = reader.ReadUe();
		bool isSeparateColourPlane = chromaFormat is 3 && reader.ReadBit() is 1;
		long width = reader.ReadUe();
		long height = reader.ReadUe();
		(long left, long right, long top, long bottom) = reader.ReadBit() is 1 ? (reader.ReadUe(), reader.ReadUe(), reader.ReadUe(), reader.ReadUe()) : (0L, 0L, 0L, 0L);

		if (reader.IsOverrun)
		{
			return null;
		}

		// 一致性窗口按色度采样单位给出
		int subWidth = !isSeparateColourPlane && chromaFormat is 1 or 2 ? 2 : 1;
		int subHeight = !isSeparateColourPlane && chromaFormat is 1 ? 2 : 1;
		return VideoResolution.Create(width - subWidth * (left + right), height - subHeight * (top + bottom));
	}

	private static void SkipScalingList(ref RbspReader reader, int size)
	{
		int last = 8;
		int next = 8;

		for (int j = 0; j < size && !reader.IsOverrun; ++j)
		{
			if (next is not 0)
			{
				next = (last + reader.ReadSe() + 256) % 256;
			}

			last = next is 0 ? last : next;
		}
	}

	private static int FindStartCode(ReadOnlySpan<byte> data, int from)
	{
		for (int i = from; i + 3 <= data.Length; ++i)
		{
			if (data[i] is 0 && data[i + 1] is 0 && data[i + 2] is 1)
			{
				return i;
			}
		}

		return -1;
	}
}
