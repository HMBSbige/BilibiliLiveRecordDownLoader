using System.Buffers.Binary;

namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// fMP4 的视频轨、视频编码和每个分片第一个视频样本是不是同步样本
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

	// 第一条视频轨的 timescale（mdhd）、默认样本时长（trex）和样本项里的宽高；没有视频轨或者读不到 timescale 时返回 null
	public static Fmp4VideoTrack? ReadVideoTrackTiming(ReadOnlySpan<byte> init)
	{
		if (!TryFindBox(init, "moov"u8, out ReadOnlySpan<byte> moov))
		{
			return null;
		}

		int offset = 0;

		while (TryReadBox(moov, ref offset, out ReadOnlySpan<byte> type, out ReadOnlySpan<byte> trak))
		{
			if (!type.SequenceEqual("trak"u8) || !IsVideoTrack(trak, out uint trackId))
			{
				continue;
			}

			// mdhd 版本 1 的创建和修改时间是 64 位
			if (!TryFindBox(trak, "mdia"u8, out ReadOnlySpan<byte> mdia) || !TryFindBox(mdia, "mdhd"u8, out ReadOnlySpan<byte> mdhd) || mdhd.Length < (mdhd[0] is 1 ? 24 : 16))
			{
				return null;
			}

			uint timescale = BinaryPrimitives.ReadUInt32BigEndian(mdhd.Slice(mdhd[0] is 1 ? 20 : 12));

			// VisualSampleEntry 的保留字段、数据引用索引和预定义字段之后是 16 位的宽和高
			VideoResolution? resolution = TryReadSampleEntry(trak, out _, out ReadOnlySpan<byte> entry, out _) && entry.Length >= 28 ? VideoResolution.Create(BinaryPrimitives.ReadUInt16BigEndian(entry.Slice(24)), BinaryPrimitives.ReadUInt16BigEndian(entry.Slice(26))) : null;
			return timescale is 0 ? null : new Fmp4VideoTrack(trackId, timescale, ReadTrackExtendsDuration(moov, trackId), resolution);
		}

		return null;
	}

	// 分片里这条轨第一个样本的解码时间（tfdt）和最后一个样本的结束时间，按轨道的 timescale 计；没有 tfdt 时返回 null
	public static (ulong Start, ulong End)? ReadFragmentTime(ReadOnlySpan<byte> fragment, Fmp4VideoTrack track)
	{
		ulong? start = null;
		ulong end = 0;
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
				if (trafType.SequenceEqual("traf"u8) && TryReadTrackFragmentTime(traf, track) is (var trafStart, var trafEnd))
				{
					start = Math.Min(start ?? trafStart, trafStart);
					end = Math.Max(end, trafEnd);
				}
			}
		}

		return start is { } value ? (value, end) : null;
	}

	private static (ulong Start, ulong End)? TryReadTrackFragmentTime(ReadOnlySpan<byte> traf, Fmp4VideoTrack track)
	{
		uint defaultDuration = track.DefaultSampleDuration;
		ulong? decodeTime = null;
		ulong duration = 0;
		int offset = 0;

		while (TryReadBox(traf, ref offset, out ReadOnlySpan<byte> type, out ReadOnlySpan<byte> content))
		{
			if (type.SequenceEqual("tfhd"u8))
			{
				if (content.Length < 8 || BinaryPrimitives.ReadUInt32BigEndian(content.Slice(4)) != track.TrackId)
				{
					return null;
				}

				// base_data_offset、sample_description_index 之后才是 default_sample_duration
				uint flags = ReadFlags(content);
				int field = 8 + ((flags & 0x01) is 0 ? 0 : 8) + ((flags & 0x02) is 0 ? 0 : 4);

				if ((flags & 0x08) is not 0 && content.Length >= field + 4)
				{
					defaultDuration = BinaryPrimitives.ReadUInt32BigEndian(content.Slice(field));
				}
			}
			else if (type.SequenceEqual("tfdt"u8) && content.Length >= (content[0] is 1 ? 12 : 8))
			{
				decodeTime = content[0] is 1 ? BinaryPrimitives.ReadUInt64BigEndian(content.Slice(4)) : BinaryPrimitives.ReadUInt32BigEndian(content.Slice(4));
			}
			else if (type.SequenceEqual("trun"u8) && content.Length >= 8)
			{
				duration += ReadRunDuration(content, defaultDuration);
			}
		}

		return decodeTime is { } start ? (start, start + duration) : null;
	}

	// 每个样本的 duration、size、flags、composition_time_offset 依次出现，各占 4 字节
	private static ulong ReadRunDuration(ReadOnlySpan<byte> trun, uint defaultDuration)
	{
		uint flags = ReadFlags(trun);
		long count = BinaryPrimitives.ReadUInt32BigEndian(trun.Slice(4));

		if ((flags & 0x100) is 0)
		{
			return (ulong)count * defaultDuration;
		}

		int field = 8 + ((flags & 0x01) is 0 ? 0 : 4) + ((flags & 0x04) is 0 ? 0 : 4);
		int stride = 4 * (1 + ((flags & 0x200) is 0 ? 0 : 1) + ((flags & 0x400) is 0 ? 0 : 1) + ((flags & 0x800) is 0 ? 0 : 1));
		ulong duration = 0;

		for (long sample = 0; sample < count && field + 4 <= trun.Length; ++sample, field += stride)
		{
			duration += BinaryPrimitives.ReadUInt32BigEndian(trun.Slice(field));
		}

		return duration;
	}

	private static uint ReadTrackExtendsDuration(ReadOnlySpan<byte> moov, uint trackId)
	{
		if (!TryFindBox(moov, "mvex"u8, out ReadOnlySpan<byte> mvex))
		{
			return 0;
		}

		int offset = 0;

		while (TryReadBox(mvex, ref offset, out ReadOnlySpan<byte> type, out ReadOnlySpan<byte> trex))
		{
			if (type.SequenceEqual("trex"u8) && trex.Length >= 16 && BinaryPrimitives.ReadUInt32BigEndian(trex.Slice(4)) == trackId)
			{
				return BinaryPrimitives.ReadUInt32BigEndian(trex.Slice(12));
			}
		}

		return 0;
	}

	// stsd 是 FullBox，版本和标志之后是 entry_count，接着才是各个样本项
	internal static bool TryReadSampleEntry(ReadOnlySpan<byte> trak, out ReadOnlySpan<byte> type, out ReadOnlySpan<byte> entry, out uint entryCount)
	{
		type = default;
		entry = default;
		entryCount = 0;

		if (!TryFindBox(trak, "mdia"u8, out ReadOnlySpan<byte> mdia) || !TryFindBox(mdia, "minf"u8, out ReadOnlySpan<byte> minf) || !TryFindBox(minf, "stbl"u8, out ReadOnlySpan<byte> stbl) || !TryFindBox(stbl, "stsd"u8, out ReadOnlySpan<byte> stsd) || stsd.Length < 8)
		{
			return false;
		}

		entryCount = BinaryPrimitives.ReadUInt32BigEndian(stsd.Slice(4));
		int offset = 8;
		return entryCount > 0 && TryReadBox(stsd, ref offset, out type, out entry);
	}

	internal static bool IsVideoTrack(ReadOnlySpan<byte> trak, out uint trackId)
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

	internal static uint ReadFlags(ReadOnlySpan<byte> fullBox)
	{
		return BinaryPrimitives.ReadUInt32BigEndian(fullBox) & 0x00FF_FFFF;
	}

	internal static bool TryFindBox(ReadOnlySpan<byte> data, ReadOnlySpan<byte> wanted, out ReadOnlySpan<byte> content)
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

	internal static bool TryReadBox(ReadOnlySpan<byte> data, scoped ref int offset, out ReadOnlySpan<byte> type, out ReadOnlySpan<byte> content)
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

// fMP4 视频轨换算时间要用的信息
internal readonly record struct Fmp4VideoTrack(uint TrackId, uint Timescale, uint DefaultSampleDuration, VideoResolution? Resolution)
{
	public TimeSpan ToTime(ulong value)
	{
		return TimeSpan.FromTicks((long)UInt128.Min((UInt128)value * TimeSpan.TicksPerSecond / Timescale, long.MaxValue));
	}
}
