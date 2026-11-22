using System.Buffers.Binary;

namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// fMP4 的视频轨、视频编码和每个分片第一个视频样本是不是同步样本
internal static class IsoBmff
{
	private const int BoxHeaderSize = 8;
	private const int LargeBoxHeaderSize = 16;
	private const uint NonSyncSampleFlag = 0x0001_0000;

	// 返回 false 表示 init 段里没有 moov；有 moov 而没有视频轨时 video 为 null
	public static bool TryReadMovie(ReadOnlySpan<byte> init, out Fmp4VideoTrack? video)
	{
		video = null;

		if (!TryFindBox(init, "moov"u8, out ReadOnlySpan<byte> moov))
		{
			return false;
		}

		int offset = 0;

		while (TryReadBox(moov, ref offset, out ReadOnlySpan<byte> type, out ReadOnlySpan<byte> trak))
		{
			if (type.SequenceEqual("trak"u8) && IsVideoTrack(trak, out uint trackId))
			{
				TrackExtends? defaults = FindTrackExtends(moov, trackId);
				video = new Fmp4VideoTrack(trackId, defaults?.DefaultSampleDuration ?? 0, defaults?.DefaultSampleFlags ?? 0, ReadTimescale(trak), ReadSampleEntrySize(trak));
				break;
			}
		}

		return true;
	}

	// 按 trun、tfhd、trex 的顺序找第一个视频样本的标志；找不到视频样本时不当作同步样本
	public static bool StartsWithSyncSample(ReadOnlySpan<byte> fragment, Fmp4VideoTrack video)
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
				if (trafType.SequenceEqual("traf"u8) && TryGetFirstSampleFlags(traf, video.TrackId, video.DefaultSampleFlags) is { } flags)
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
				int field = 8 + ((flags & TrackFragmentFlags.BaseDataOffsetPresent) is 0 ? 0 : 8) + ((flags & TrackFragmentFlags.SampleDescriptionIndexPresent) is 0 ? 0 : 4) + ((flags & TrackFragmentFlags.DefaultSampleDurationPresent) is 0 ? 0 : 4) + ((flags & TrackFragmentFlags.DefaultSampleSizePresent) is 0 ? 0 : 4);

				if ((flags & TrackFragmentFlags.DefaultSampleFlagsPresent) is not 0)
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
				int field = 8 + ((flags & TrackRunFlags.DataOffsetPresent) is 0 ? 0 : 4);

				if ((flags & TrackRunFlags.FirstSampleFlagsPresent) is not 0)
				{
					return content.Length >= field + 4 ? BinaryPrimitives.ReadUInt32BigEndian(content.Slice(field)) : null;
				}

				if ((flags & TrackRunFlags.SampleFlagsPresent) is not 0)
				{
					// 每个样本的 duration、size 在 flags 之前
					field += ((flags & TrackRunFlags.SampleDurationPresent) is 0 ? 0 : 4) + ((flags & TrackRunFlags.SampleSizePresent) is 0 ? 0 : 4);
					return content.Length >= field + 4 ? BinaryPrimitives.ReadUInt32BigEndian(content.Slice(field)) : null;
				}

				return defaultFlags;
			}
		}

		return null;
	}

	// mdhd 版本 1 的创建和修改时间是 64 位；为 0 的 timescale 换算不了时间
	private static uint? ReadTimescale(ReadOnlySpan<byte> trak)
	{
		if (!TryFindBox(trak, "mdia"u8, out ReadOnlySpan<byte> mdia) || !TryFindBox(mdia, "mdhd"u8, out ReadOnlySpan<byte> mdhd))
		{
			return null;
		}

		int field = mdhd is [1, ..] ? 20 : 12;
		return mdhd.Length >= field + 4 && BinaryPrimitives.ReadUInt32BigEndian(mdhd.Slice(field)) is var timescale and not 0 ? timescale : null;
	}

	// VisualSampleEntry 的保留字段、数据引用索引和预定义字段之后是 16 位的宽和高
	private static VideoResolution? ReadSampleEntrySize(ReadOnlySpan<byte> trak)
	{
		return TryReadSampleEntry(trak, out _, out ReadOnlySpan<byte> entry, out _) && entry.Length >= 28 ? VideoResolution.Create(BinaryPrimitives.ReadUInt16BigEndian(entry.Slice(24)), BinaryPrimitives.ReadUInt16BigEndian(entry.Slice(26))) : null;
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
				int field = 8 + ((flags & TrackFragmentFlags.BaseDataOffsetPresent) is 0 ? 0 : 8) + ((flags & TrackFragmentFlags.SampleDescriptionIndexPresent) is 0 ? 0 : 4);

				if ((flags & TrackFragmentFlags.DefaultSampleDurationPresent) is not 0 && content.Length >= field + 4)
				{
					defaultDuration = BinaryPrimitives.ReadUInt32BigEndian(content.Slice(field));
				}
			}
			else if (type.SequenceEqual("tfdt"u8) && content.Length >= (content is [1, ..] ? 12 : 8))
			{
				decodeTime = content is [1, ..] ? BinaryPrimitives.ReadUInt64BigEndian(content.Slice(4)) : BinaryPrimitives.ReadUInt32BigEndian(content.Slice(4));
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

		if ((flags & TrackRunFlags.SampleDurationPresent) is 0)
		{
			return (ulong)count * defaultDuration;
		}

		int field = 8 + ((flags & TrackRunFlags.DataOffsetPresent) is 0 ? 0 : 4) + ((flags & TrackRunFlags.FirstSampleFlagsPresent) is 0 ? 0 : 4);
		int stride = 4 * (1 + ((flags & TrackRunFlags.SampleSizePresent) is 0 ? 0 : 1) + ((flags & TrackRunFlags.SampleFlagsPresent) is 0 ? 0 : 1) + ((flags & TrackRunFlags.SampleCompositionTimeOffsetPresent) is 0 ? 0 : 1));
		ulong duration = 0;

		for (long sample = 0; sample < count && field + 4 <= trun.Length; ++sample, field += stride)
		{
			duration += BinaryPrimitives.ReadUInt32BigEndian(trun.Slice(field));
		}

		return duration;
	}

	// version/flags 之后依次是 track_ID、default_sample_description_index、default_sample_duration、default_sample_size、default_sample_flags
	internal static TrackExtends? ReadTrackExtends(ReadOnlySpan<byte> trex)
	{
		return trex.Length < 24 ? null : new TrackExtends(BinaryPrimitives.ReadUInt32BigEndian(trex.Slice(4)), BinaryPrimitives.ReadUInt32BigEndian(trex.Slice(12)), BinaryPrimitives.ReadUInt32BigEndian(trex.Slice(16)), BinaryPrimitives.ReadUInt32BigEndian(trex.Slice(20)));
	}

	private static TrackExtends? FindTrackExtends(ReadOnlySpan<byte> moov, uint trackId)
	{
		if (!TryFindBox(moov, "mvex"u8, out ReadOnlySpan<byte> mvex))
		{
			return null;
		}

		int offset = 0;

		while (TryReadBox(mvex, ref offset, out ReadOnlySpan<byte> type, out ReadOnlySpan<byte> trex))
		{
			if (type.SequenceEqual("trex"u8) && ReadTrackExtends(trex) is { } defaults && defaults.TrackId == trackId)
			{
				return defaults;
			}
		}

		return null;
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

// init 段里第一条视频轨：trex 给的默认样本时长和标志，mdhd 的 timescale（读不到时为 null），样本项里的宽高
internal readonly record struct Fmp4VideoTrack(uint TrackId, uint DefaultSampleDuration, uint DefaultSampleFlags, uint? Timescale, VideoResolution? Resolution)
{
	public static TimeSpan ToTime(ulong value, uint timescale)
	{
		return TimeSpan.FromTicks((long)UInt128.Min((UInt128)value * TimeSpan.TicksPerSecond / timescale, long.MaxValue));
	}
}

internal readonly record struct TrackExtends(uint TrackId, uint DefaultSampleDuration, uint DefaultSampleSize, uint DefaultSampleFlags);

// tfhd 的标志位，track_ID 之后的可选字段按这个顺序出现
internal static class TrackFragmentFlags
{
	public const uint BaseDataOffsetPresent = 0x00_0001;
	public const uint SampleDescriptionIndexPresent = 0x00_0002;
	public const uint DefaultSampleDurationPresent = 0x00_0008;
	public const uint DefaultSampleSizePresent = 0x00_0010;
	public const uint DefaultSampleFlagsPresent = 0x00_0020;
	public const uint DefaultBaseIsMoof = 0x02_0000;
}

// trun 的标志位：data_offset 和 first_sample_flags 跟在 sample_count 之后，其余四个是每个样本依次带的字段
internal static class TrackRunFlags
{
	public const uint DataOffsetPresent = 0x00_0001;
	public const uint FirstSampleFlagsPresent = 0x00_0004;
	public const uint SampleDurationPresent = 0x00_0100;
	public const uint SampleSizePresent = 0x00_0200;
	public const uint SampleFlagsPresent = 0x00_0400;
	public const uint SampleCompositionTimeOffsetPresent = 0x00_0800;
}
