using System.Buffers.Binary;

namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// fMP4 的视频轨和每个分片第一个视频样本是不是同步样本
internal static class IsoBmff
{
	private const int BoxHeaderSize = 8;
	private const int LargeBoxHeaderSize = 16;
	private const uint NonSyncSampleFlag = 0x0001_0000;

	// 返回 false 表示 init 段里没有 moov；有 moov 而没有视频轨时 videoTrackId 为 null
	public static bool TryReadVideoTrack(ReadOnlySpan<byte> init, out uint? videoTrackId, out uint defaultSampleFlags)
	{
		videoTrackId = null;
		defaultSampleFlags = 0;

		if (!TryFindBox(init, "moov"u8, out ReadOnlySpan<byte> moov))
		{
			return false;
		}

		int offset = 0;

		while (TryReadBox(moov, ref offset, out ReadOnlySpan<byte> type, out ReadOnlySpan<byte> content))
		{
			if (type.SequenceEqual("trak"u8) && videoTrackId is null && IsVideoTrack(content, out uint trackId))
			{
				videoTrackId = trackId;
			}
		}

		if (videoTrackId is { } id && TryFindBox(moov, "mvex"u8, out ReadOnlySpan<byte> mvex))
		{
			offset = 0;

			while (TryReadBox(mvex, ref offset, out ReadOnlySpan<byte> type, out ReadOnlySpan<byte> trex))
			{
				// version/flags、track_ID、default_sample_description_index、default_sample_duration、default_sample_size、default_sample_flags
				if (type.SequenceEqual("trex"u8) && trex.Length >= 24 && BinaryPrimitives.ReadUInt32BigEndian(trex.Slice(4)) == id)
				{
					defaultSampleFlags = BinaryPrimitives.ReadUInt32BigEndian(trex.Slice(20));
				}
			}
		}

		return true;
	}

	// 按 trun、tfhd、trex 的顺序找第一个视频样本的标志；找不到视频样本时不当作同步样本
	public static bool StartsWithSyncSample(ReadOnlySpan<byte> fragment, uint videoTrackId, uint trexSampleFlags)
	{
		int offset = 0;

		while (TryReadBox(fragment, ref offset, out ReadOnlySpan<byte> type, out ReadOnlySpan<byte> moof))
		{
			if (!type.SequenceEqual("moof"u8))
			{
				continue;
			}

			int trafOffset = 0;

			while (TryReadBox(moof, ref trafOffset, out ReadOnlySpan<byte> trafType, out ReadOnlySpan<byte> traf))
			{
				if (trafType.SequenceEqual("traf"u8) && TryGetFirstSampleFlags(traf, videoTrackId, trexSampleFlags) is { } flags)
				{
					return (flags & NonSyncSampleFlag) is 0;
				}
			}
		}

		return false;
	}

	private static uint? TryGetFirstSampleFlags(ReadOnlySpan<byte> traf, uint videoTrackId, uint trexSampleFlags)
	{
		uint defaultFlags = trexSampleFlags;
		bool isVideo = false;
		int offset = 0;

		while (TryReadBox(traf, ref offset, out ReadOnlySpan<byte> type, out ReadOnlySpan<byte> content))
		{
			if (type.SequenceEqual("tfhd"u8))
			{
				if (content.Length < 8)
				{
					return null;
				}

				uint flags = ReadFlags(content);
				isVideo = BinaryPrimitives.ReadUInt32BigEndian(content.Slice(4)) == videoTrackId;

				if (!isVideo)
				{
					return null;
				}

				// base_data_offset、sample_description_index、default_sample_duration、default_sample_size 之后才是 default_sample_flags
				int field = 8 + ((flags & 0x01) is 0 ? 0 : 8) + ((flags & 0x02) is 0 ? 0 : 4) + ((flags & 0x08) is 0 ? 0 : 4) + ((flags & 0x10) is 0 ? 0 : 4);

				if ((flags & 0x20) is not 0)
				{
					if (content.Length < field + 4)
					{
						return null;
					}

					defaultFlags = BinaryPrimitives.ReadUInt32BigEndian(content.Slice(field));
				}
			}
			else if (type.SequenceEqual("trun"u8) && isVideo && content.Length >= 8 && BinaryPrimitives.ReadUInt32BigEndian(content.Slice(4)) > 0)
			{
				uint flags = ReadFlags(content);
				int field = 8 + ((flags & 0x01) is 0 ? 0 : 4);

				if ((flags & 0x04) is not 0)
				{
					return content.Length >= field + 4 ? BinaryPrimitives.ReadUInt32BigEndian(content.Slice(field)) : null;
				}

				if ((flags & 0x400) is not 0)
				{
					// 每个样本的 duration、size 在 flags 之前
					field += ((flags & 0x100) is 0 ? 0 : 4) + ((flags & 0x200) is 0 ? 0 : 4);
					return content.Length >= field + 4 ? BinaryPrimitives.ReadUInt32BigEndian(content.Slice(field)) : null;
				}

				return defaultFlags;
			}
		}

		return null;
	}

	private static bool IsVideoTrack(ReadOnlySpan<byte> trak, out uint trackId)
	{
		trackId = 0;

		if (!TryFindBox(trak, "tkhd"u8, out ReadOnlySpan<byte> tkhd) || !TryFindBox(trak, "mdia"u8, out ReadOnlySpan<byte> mdia) || !TryFindBox(mdia, "hdlr"u8, out ReadOnlySpan<byte> hdlr))
		{
			return false;
		}

		// tkhd 版本 1 的创建和修改时间是 64 位
		int trackIdOffset = tkhd is [1, ..] ? 20 : 12;

		if (tkhd.Length < trackIdOffset + 4 || hdlr.Length < 12)
		{
			return false;
		}

		trackId = BinaryPrimitives.ReadUInt32BigEndian(tkhd.Slice(trackIdOffset));
		return hdlr.Slice(8, 4).SequenceEqual("vide"u8);
	}

	private static uint ReadFlags(ReadOnlySpan<byte> fullBox)
	{
		return BinaryPrimitives.ReadUInt32BigEndian(fullBox) & 0x00FF_FFFF;
	}

	private static bool TryFindBox(ReadOnlySpan<byte> data, ReadOnlySpan<byte> wanted, out ReadOnlySpan<byte> content)
	{
		int offset = 0;

		while (TryReadBox(data, ref offset, out ReadOnlySpan<byte> type, out content))
		{
			if (type.SequenceEqual(wanted))
			{
				return true;
			}
		}

		content = default;
		return false;
	}

	private static bool TryReadBox(ReadOnlySpan<byte> data, scoped ref int offset, out ReadOnlySpan<byte> type, out ReadOnlySpan<byte> content)
	{
		type = default;
		content = default;
		ReadOnlySpan<byte> rest = data.Slice(offset);

		if (rest.Length < BoxHeaderSize)
		{
			return false;
		}

		ulong size = BinaryPrimitives.ReadUInt32BigEndian(rest);
		int headerSize = BoxHeaderSize;

		if (size is 1)
		{
			if (rest.Length < LargeBoxHeaderSize)
			{
				return false;
			}

			size = BinaryPrimitives.ReadUInt64BigEndian(rest.Slice(BoxHeaderSize));
			headerSize = LargeBoxHeaderSize;
		}
		else if (size is 0)
		{
			size = (ulong)rest.Length;
		}

		if (size < (ulong)headerSize || size > (ulong)rest.Length)
		{
			return false;
		}

		type = rest.Slice(4, 4);
		content = rest.Slice(headerSize, (int)size - headerSize);
		offset += (int)size;
		return true;
	}
}
