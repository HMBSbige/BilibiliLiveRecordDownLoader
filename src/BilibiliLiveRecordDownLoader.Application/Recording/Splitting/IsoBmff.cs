using System.Buffers.Binary;
using System.Numerics;

namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

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
				video = new Fmp4VideoTrack(trackId, FindTrackExtends(moov, trackId)?.DefaultSampleFlags ?? 0, ReadSampleEntrySize(trak));
				break;
			}
		}

		return true;
	}

	// 样本标志依次取 trun、tfhd、trex 里的；找不到视频样本时不当作同步样本
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
				if (trafType.SequenceEqual("traf"u8) && ReadFirstSampleFlags(traf, video.TrackId, video.DefaultSampleFlags) is { } flags)
				{
					return (flags & NonSyncSampleFlag) is 0;
				}
			}
		}

		return false;
	}

	// 不是视频轨的 traf 和读不全的返回 null
	private static uint? ReadFirstSampleFlags(ReadOnlySpan<byte> traf, uint videoTrackId, uint trexSampleFlags)
	{
		uint defaultFlags = trexSampleFlags;
		bool isVideo = false;
		int offset = 0;

		while (TryReadBox(traf, ref offset, out ReadOnlySpan<byte> type, out ReadOnlySpan<byte> content))
		{
			if (type.SequenceEqual("tfhd"u8))
			{
				if (content.Length < 8 || BinaryPrimitives.ReadUInt32BigEndian(content.Slice(4)) != videoTrackId)
				{
					return null;
				}

				isVideo = true;
				uint flags = ReadFlags(content);
				int field = TrackFragmentFlags.OffsetOf(flags, TrackFragmentFlags.DefaultSampleFlagsPresent);

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
				uint source = (flags & TrackRunFlags.FirstSampleFlagsPresent) is not 0 ? TrackRunFlags.FirstSampleFlagsPresent : TrackRunFlags.SampleFlagsPresent;

				if ((flags & source) is 0)
				{
					return defaultFlags;
				}

				int field = TrackRunFlags.OffsetOf(flags, source);
				return content.Length >= field + 4 ? BinaryPrimitives.ReadUInt32BigEndian(content.Slice(field)) : null;
			}
		}

		return null;
	}

	// mdhd 版本 1 的创建和修改时间是 64 位
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

	public static Fmp4Track[] ReadTracks(ReadOnlySpan<byte> moov)
	{
		List<Fmp4Track> tracks = [];
		int offset = 0;

		while (TryReadBox(moov, ref offset, out ReadOnlySpan<byte> type, out ReadOnlySpan<byte> trak))
		{
			if (type.SequenceEqual("trak"u8) && TryReadTrackId(trak, out uint trackId) && ReadTimescale(trak) is { } timescale)
			{
				tracks.Add(new Fmp4Track(trackId, timescale, FindTrackExtends(moov, trackId)?.DefaultSampleDuration ?? 0));
			}
		}

		return [.. tracks];
	}

	// trafOffset 是 traf 内容在 moof 内容里的位置；版本 0 的 tfdt 给的是 32 位原值，由 Fmp4TrackTimes 展开
	public static TrackFragmentTime? ReadTrackFragmentTime(ReadOnlySpan<byte> traf, int trafOffset, ReadOnlySpan<Fmp4Track> tracks)
	{
		int track = -1;
		uint defaultDuration = 0;
		ulong? decodeTime = null;
		int tfdtOffset = 0;
		ulong duration = 0;
		int offset = 0;

		while (TryReadBox(traf, ref offset, out ReadOnlySpan<byte> type, out ReadOnlySpan<byte> content))
		{
			if (type.SequenceEqual("tfhd"u8))
			{
				track = content.Length < 8 ? -1 : Find(tracks, BinaryPrimitives.ReadUInt32BigEndian(content.Slice(4)));

				if (track < 0)
				{
					return null;
				}

				defaultDuration = tracks[track].DefaultSampleDuration;

				uint flags = ReadFlags(content);
				int field = TrackFragmentFlags.OffsetOf(flags, TrackFragmentFlags.DefaultSampleDurationPresent);

				if ((flags & TrackFragmentFlags.DefaultSampleDurationPresent) is not 0 && content.Length >= field + 4)
				{
					defaultDuration = BinaryPrimitives.ReadUInt32BigEndian(content.Slice(field));
				}
			}
			else if (type.SequenceEqual("tfdt"u8) && content.Length >= (content is [1, ..] ? 12 : 8))
			{
				decodeTime = content is [1, ..] ? BinaryPrimitives.ReadUInt64BigEndian(content.Slice(4)) : BinaryPrimitives.ReadUInt32BigEndian(content.Slice(4));
				tfdtOffset = trafOffset + offset - content.Length;
			}
			else if (type.SequenceEqual("trun"u8) && content.Length >= 8)
			{
				duration += ReadRunDuration(content, defaultDuration);
			}
		}

		return track >= 0 && decodeTime is { } start ? new TrackFragmentTime(track, tracks[track], start, duration, tfdtOffset) : null;

		static int Find(ReadOnlySpan<Fmp4Track> tracks, uint trackId)
		{
			for (int index = 0; index < tracks.Length; ++index)
			{
				if (tracks[index].TrackId == trackId)
				{
					return index;
				}
			}

			return -1;
		}
	}

	private static ulong ReadRunDuration(ReadOnlySpan<byte> trun, uint defaultDuration)
	{
		uint flags = ReadFlags(trun);
		long count = BinaryPrimitives.ReadUInt32BigEndian(trun.Slice(4));

		if ((flags & TrackRunFlags.SampleDurationPresent) is 0)
		{
			return (ulong)count * defaultDuration;
		}

		int field = TrackRunFlags.OffsetOf(flags, TrackRunFlags.SampleDurationPresent);
		int stride = TrackRunFlags.SampleFieldsSize(flags);
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
		return TryReadTrackId(trak, out trackId) && TryFindBox(trak, "mdia"u8, out ReadOnlySpan<byte> mdia) && TryFindBox(mdia, "hdlr"u8, out ReadOnlySpan<byte> hdlr) && hdlr.Length >= 12 && hdlr.Slice(8, 4).SequenceEqual("vide"u8);
	}

	private static bool TryReadTrackId(ReadOnlySpan<byte> trak, out uint trackId)
	{
		trackId = 0;

		// tkhd 版本 1 的创建和修改时间是 64 位
		if (!TryFindBox(trak, "tkhd"u8, out ReadOnlySpan<byte> tkhd) || tkhd.Length < (tkhd is [1, ..] ? 24 : 16))
		{
			return false;
		}

		trackId = BinaryPrimitives.ReadUInt32BigEndian(tkhd.Slice(tkhd is [1, ..] ? 20 : 12));
		return true;
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

internal readonly record struct Fmp4VideoTrack(uint TrackId, uint DefaultSampleFlags, VideoResolution? Resolution);

// Timescale 不为 0
internal readonly record struct Fmp4Track(uint TrackId, uint Timescale, uint DefaultSampleDuration)
{
	// 向下取整
	public long ToTicks(ulong value)
	{
		return (long)UInt128.Min((UInt128)value * TimeSpan.TicksPerSecond / Timescale, long.MaxValue);
	}

	// 不小于 ticks 的最小轨道时间，ticks 为负时是负的；按它减去起点不会减出负数
	public long FromTicksCeiling(long ticks)
	{
		(Int128 quotient, Int128 remainder) = Int128.DivRem((Int128)ticks * Timescale, TimeSpan.TicksPerSecond);
		return (long)(remainder > 0 ? quotient + 1 : quotient);
	}
}

// 时间按轨道的 timescale 计；TfdtOffset 是 tfdt 内容在 moof 内容里的位置
internal readonly record struct TrackFragmentTime(int TrackIndex, Fmp4Track Track, ulong DecodeTime, ulong Duration, int TfdtOffset);

internal readonly record struct TrackExtends(uint TrackId, uint DefaultSampleDuration, uint DefaultSampleSize, uint DefaultSampleFlags);

// track_ID 之后的可选字段按标志位从低到高的顺序出现，base_data_offset 占 8 字节，其余各占 4 字节
internal static class TrackFragmentFlags
{
	public const uint BaseDataOffsetPresent = 0x00_0001;
	public const uint SampleDescriptionIndexPresent = 0x00_0002;
	public const uint DefaultSampleDurationPresent = 0x00_0008;
	public const uint DefaultSampleSizePresent = 0x00_0010;
	public const uint DefaultSampleFlagsPresent = 0x00_0020;
	public const uint DefaultBaseIsMoof = 0x02_0000;

	private const uint OptionalFields = BaseDataOffsetPresent | SampleDescriptionIndexPresent | DefaultSampleDurationPresent | DefaultSampleSizePresent | DefaultSampleFlagsPresent;

	// field 是某个可选字段的标志，返回这个字段在 tfhd 内容里的位置，不管它在不在
	public static int OffsetOf(uint flags, uint field)
	{
		uint preceding = flags & OptionalFields & field - 1;
		return 8 + 4 * BitOperations.PopCount(preceding) + ((preceding & BaseDataOffsetPresent) is 0 ? 0 : 4);
	}
}

// sample_count 之后的字段按标志位从低到高的顺序出现，各占 4 字节：可选的 data_offset 和 first_sample_flags，然后每个样本依次带标志选中的那几个
internal static class TrackRunFlags
{
	public const uint DataOffsetPresent = 0x00_0001;
	public const uint FirstSampleFlagsPresent = 0x00_0004;
	public const uint SampleDurationPresent = 0x00_0100;
	public const uint SampleSizePresent = 0x00_0200;
	public const uint SampleFlagsPresent = 0x00_0400;
	public const uint SampleCompositionTimeOffsetPresent = 0x00_0800;

	private const uint SampleFields = SampleDurationPresent | SampleSizePresent | SampleFlagsPresent | SampleCompositionTimeOffsetPresent;

	// 返回 field 对应的字段在 trun 内容里的位置，样本字段指第一个样本的，不管它在不在；第一个样本从 SampleDurationPresent 的位置开始
	public static int OffsetOf(uint flags, uint field)
	{
		return 8 + 4 * BitOperations.PopCount(flags & (DataOffsetPresent | FirstSampleFlagsPresent | SampleFields) & field - 1);
	}

	public static int SampleFieldsSize(uint flags)
	{
		return 4 * BitOperations.PopCount(flags & SampleFields);
	}
}
