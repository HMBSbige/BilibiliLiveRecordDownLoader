using Microsoft.Win32.SafeHandles;
using System.Buffers.Binary;

namespace BilibiliLiveRecordDownLoader.Application.Recording.Hybrid;

internal closed record FragmentedFile
{
	// 开头的 moov 不带 mvex：已经转换过，或者本来就是普通 MP4
	public sealed record Plain : FragmentedFile;

	public sealed record Fragmented(FragmentedLayout Layout) : FragmentedFile;
}

internal readonly record struct ByteRange(long Start, long End);

// DataEnd 之前是录下来的原始字节，之后是上次转换中断留下的尾巴
// MovieContent 是 init 段 moov 的内容，不含盒子头；MediaRanges 是各个 mdat 的数据部分，按位置排序；TrunSamples 是所有 trun 的样本数，丢掉的也算
internal sealed record FragmentedLayout(int FileTypeSize, long MovieStart, ReadOnlyMemory<byte> MovieContent, long DataEnd, IReadOnlyList<ByteRange> MediaRanges, IReadOnlyList<TrackIndex> Tracks, long TrunSamples)
{
	public long DroppedSamples => Tracks.Sum(static track => track.DroppedSamples);

	public long RetimedSamples => Tracks.Sum(static track => track.RetimedSamples);
}

// 只读盒子头和 moof，媒体数据不读
internal static class FragmentedFileReader
{
	private const int BoxHeaderSize = 8;
	private const int LargeBoxHeaderSize = 16;

	// B 站的 moof 只有几百字节，一次读进 moof 和后面 mdat 的头
	private const int WindowSize = 4096;

	// init 段和单个 moof 都不会大到这个程度，超过了多半是坏文件
	private const int MaxBoxReadSize = 64 << 20;

	// 十小时 60 fps 的视频加 48 kHz 的 AAC 约 400 万个样本；不带逐样本字段的 trun 不占空间，样本数不受盒子大小约束，不限制总数的话坏文件能空转几十亿次，轨道的样本下标也会超出 int
	private const long MaxSamples = 1 << 28;

	// 头部备份的 uuid 比 MovieStart 多出的字节：盒子头、标记、原长度，以及备份进去的原 moov 开头
	private static int BackupOverhead => BoxHeaderSize + HybridMovie.Signature.Length + sizeof(long) + HybridMovie.MediaDataHeaderSize;

	public static FragmentedFile Read(SafeFileHandle handle)
	{
		FileWindow window = new(handle, RandomAccess.GetLength(handle));

		if (window.ReadHeader(0) is not { Type: BoxType.Ftyp, HeaderSize: BoxHeaderSize, Size: >= 16 } ftyp || ftyp.End > window.Length)
		{
			throw new UnsupportedLayoutException("the file does not start with ftyp");
		}

		long position = ftyp.End;
		bool hasMediaData = false;
		BoxHeader movieHeader;

		while (true)
		{
			if (window.ReadHeader(position) is not { } header || header.End > window.Length)
			{
				throw new UnsupportedLayoutException("no moov");
			}

			if (header.Type is BoxType.Moov)
			{
				movieHeader = header;
				break;
			}

			if (header.Type is BoxType.Moof)
			{
				throw new UnsupportedLayoutException("moof before moov");
			}

			hasMediaData |= header.Type is BoxType.Mdat;
			position = header.End;
		}

		// 转换过的长录制，moov 里的样本表能有几十 MB，只看盒子头
		if (!window.HasChild(movieHeader, BoxType.Mvex))
		{
			return new FragmentedFile.Plain();
		}

		// 普通 MP4 的 moov 不会带 mvex，mdat 在前说明不是本程序录的分片 MP4
		if (hasMediaData)
		{
			throw new UnsupportedLayoutException("mdat before a fragmented moov");
		}

		// 转换的最后一步要一次改写 moov 之前的部分和 mdat 头，只有在一个扇区里才不会写一半；放在扫描分片之前，超出时不用白扫一遍
		if (movieHeader.Start + HybridMovie.MediaDataHeaderSize > HybridMovie.MaxHeadSize)
		{
			throw new UnsupportedLayoutException($"the head up to the moov at {movieHeader.Start} does not fit in one sector");
		}

		byte[] movie = window.ReadContent(movieHeader);
		long recordedEnd = FindRecordedEnd(window, movieHeader) ?? window.Length;
		return new FragmentedFile.Fragmented(ReadFragments(handle, recordedEnd, (int)ftyp.Size, movieHeader, movie));
	}

	// 上次转换写完新 moov 后被打断时，文件末尾是追加的 [章节 mdat][free 或已经翻成 moov 的新 moov]，新 moov 的头部备份记着追加前的文件长度
	// 录制时最后一个盒子没写完的话，追加的盒子不在盒子边界上，扫描认不出来，只能靠它
	// 头部备份是新 moov 的 udta 里最后一个盒子，之后只有从原 moov 原样复制的盒子，加起来不比原 moov 长，所以只在末尾 movieHeader.End + BackupOverhead 字节里找
	private static long? FindRecordedEnd(FileWindow window, BoxHeader movieHeader)
	{
		long size = Math.Min(window.Length - movieHeader.End, Math.Min(movieHeader.End + BackupOverhead, MaxBoxReadSize));
		long from = window.Length - size;
		ReadOnlySpan<byte> tail = window.Read(from, (int)size);
		int signature = tail.LastIndexOf(HybridMovie.Signature);

		if (signature < 4 || BinaryPrimitives.ReadUInt32BigEndian(tail.Slice(signature - 4)) is not BoxType.Uuid || tail.Length - signature < HybridMovie.Signature.Length + 8)
		{
			return null;
		}

		long end = BinaryPrimitives.ReadInt64BigEndian(tail.Slice(signature + HybridMovie.Signature.Length));

		if (end < movieHeader.End || end >= from + signature)
		{
			return null;
		}

		long position = end;

		while (position < window.Length)
		{
			if (window.ReadHeader(position) is not { } box || box.End > window.Length || !IsLeftover(window, box))
			{
				return null;
			}

			position = box.End;
		}

		return end;
	}

	private static FragmentedLayout ReadFragments(SafeFileHandle handle, long length, int fileTypeSize, BoxHeader movieHeader, byte[] movie)
	{
		(TrackIndex[] tracks, TrackSource[] sources) = ReadTracks(movie);
		Scanner scanner = new(new FileWindow(handle, length), tracks, sources, IsoBmff.ReadTracks(movie));
		long dataEnd = scanner.Scan(movieHeader.End);

		// 扫到上次转换追加的盒子时，样本是按包含尾巴的长度检查的；有样本伸进尾巴，就按尾巴之前的长度重读一遍
		if (dataEnd < length && scanner.SampleEnd > dataEnd)
		{
			return ReadFragments(handle, dataEnd, fileTypeSize, movieHeader, movie);
		}

		foreach (TrackIndex track in tracks)
		{
			track.Complete();
		}

		if (!tracks.Any(static track => track.SampleCount > 0))
		{
			throw new UnsupportedLayoutException("no samples");
		}

		return new FragmentedLayout(fileTypeSize, movieHeader.Start, movie, dataEnd, scanner.MediaRanges, tracks, scanner.TrunSamples);
	}

	// 上次转换中断时留下的：放章节的 mdat，以及还是 free 的或已经翻成 moov 的新 moov
	private static bool IsLeftover(FileWindow window, BoxHeader header)
	{
		return header.Type switch
		{
			BoxType.Moov => true,
			BoxType.Free => window.Read(header.ContentStart, 8) is { Length: 8 } content && BinaryPrimitives.ReadUInt32BigEndian(content.Slice(4)) is BoxType.Mvhd,
			BoxType.Mdat => window.Read(header.ContentStart, HybridMovie.Signature.Length).SequenceEqual(HybridMovie.Signature),
			_ => false
		};
	}

	private static (TrackIndex[] Tracks, TrackSource[] Sources) ReadTracks(ReadOnlySpan<byte> movie)
	{
		if (!IsoBmff.TryFindBox(movie, BoxType.Mvhd, out ReadOnlySpan<byte> mvhd))
		{
			throw new UnsupportedLayoutException("no mvhd");
		}

		// mvhd 版本 1 的创建和修改时间是 64 位
		int field = mvhd is [1, ..] ? 20 : 12;

		if (mvhd.Length < field + 4)
		{
			throw new UnsupportedLayoutException("truncated mvhd");
		}

		if (IsoBmff.TryFindBox(movie, BoxType.Pssh, out _))
		{
			throw new UnsupportedLayoutException("encrypted content");
		}

		uint movieTimescale = BinaryPrimitives.ReadUInt32BigEndian(mvhd.Slice(field));
		Dictionary<uint, TrackExtends> extends = IsoBmff.ReadTrackExtends(movie);
		List<(TrackIndex Track, TrackSource Source)> tracks = [];
		int offset = 0;

		while (IsoBmff.TryReadBox(movie, ref offset, out uint type, out ReadOnlySpan<byte> trak))
		{
			if (type is BoxType.Trak)
			{
				tracks.Add(ReadTrack(trak, movieTimescale, extends));
			}
		}

		if (tracks.DistinctBy(static track => track.Track.TrackId).Count() != tracks.Count)
		{
			throw new UnsupportedLayoutException("duplicate track IDs");
		}

		return ([.. tracks.Select(static track => track.Track)], [.. tracks.Select(static track => track.Source)]);
	}

	private static (TrackIndex Track, TrackSource Source) ReadTrack(ReadOnlySpan<byte> trak, uint movieTimescale, Dictionary<uint, TrackExtends> extends)
	{
		if (!IsoBmff.TryReadTrackId(trak, out uint trackId) || IsoBmff.ReadTimescale(trak) is not { } timescale)
		{
			throw new UnsupportedLayoutException("a track without tkhd or mdhd");
		}

		if (!IsoBmff.TryFindBox(trak, BoxType.Mdia, out ReadOnlySpan<byte> mdia) || !IsoBmff.TryFindBox(mdia, BoxType.Hdlr, out ReadOnlySpan<byte> hdlr) || IsoBmff.ReadHandlerType(hdlr) is not { } handler)
		{
			throw new UnsupportedLayoutException($"track {trackId} has no hdlr");
		}

		TrackKind kind = handler switch
		{
			BoxType.Vide => TrackKind.Video,
			BoxType.Soun => TrackKind.Audio,
			_ => throw new UnsupportedLayoutException($"track {trackId} is neither video nor audio")
		};

		if (!IsoBmff.TryFindBox(mdia, BoxType.Minf, out ReadOnlySpan<byte> minf) || !IsoBmff.TryFindBox(minf, BoxType.Stbl, out ReadOnlySpan<byte> stbl) || !IsoBmff.TryReadSampleEntry(trak, out uint entryType, out ReadOnlySpan<byte> entry, out uint descriptionCount))
		{
			throw new UnsupportedLayoutException($"track {trackId} has no sample description");
		}

		// 本程序录的 init 段从来不带样本；带了的话它们排在所有分片之前，这里只读分片，不转换
		if (IsoBmff.TryFindBox(stbl, BoxType.Stsz, out ReadOnlySpan<byte> stsz) && (stsz.Length < 12 || BinaryPrimitives.ReadUInt32BigEndian(stsz.Slice(8)) is not 0))
		{
			throw new UnsupportedLayoutException($"track {trackId} has samples in the init segment");
		}

		if (entryType is BoxType.Encv or BoxType.Enca)
		{
			throw new UnsupportedLayoutException("encrypted content");
		}

		bool isAac = kind is TrackKind.Audio && IsAac(entryType, entry);
		(long timeOffset, long presentationFloor) = ReadEditList(trak, trackId, movieTimescale, timescale);
		TrackExtends defaults = extends.GetValueOrDefault(trackId, new TrackExtends(trackId, 1, 0, 0, 0));

		return (new TrackIndex(trackId, kind, timescale, presentationFloor, entryType, isAac, trak.ToArray()), new TrackSource(defaults, descriptionCount, timeOffset));
	}

	// init 段的编辑列表对分片里的样本同样有效：显示时间 = tfdt + 合成偏移 - 媒体起点 + 空编辑时长，早于空编辑结尾的部分不显示（比如 AAC 的编码延迟）
	// 没有编辑列表时媒体时间线从 0 开始显示；媒体编辑的时长不看，写 init 段时还不知道分片有多长
	// 只认一段可选的空编辑加一段速率为 1 的媒体编辑，更复杂的编辑列表套不到分片上，不转换
	private static (long TimeOffset, long PresentationFloor) ReadEditList(ReadOnlySpan<byte> trak, uint trackId, uint movieTimescale, uint timescale)
	{
		if (!IsoBmff.TryFindBox(trak, BoxType.Edts, out ReadOnlySpan<byte> edts) || !IsoBmff.TryFindBox(edts, BoxType.Elst, out ReadOnlySpan<byte> elst) || elst.Length < 8)
		{
			return (0, 0);
		}

		bool isLong = elst[0] is 1;
		int entrySize = isLong ? 20 : 12;
		uint count = BinaryPrimitives.ReadUInt32BigEndian(elst.Slice(4));

		if (count > 2 || 8 + count * entrySize > elst.Length)
		{
			throw new UnsupportedLayoutException($"track {trackId} has an edit list that cannot be applied to fragments");
		}

		long empty = 0;
		long? mediaTime = null;

		for (int index = 0; index < count; ++index)
		{
			ReadOnlySpan<byte> item = elst.Slice(8 + index * entrySize);
			long duration = isLong ? (long)BinaryPrimitives.ReadUInt64BigEndian(item) : BinaryPrimitives.ReadUInt32BigEndian(item);
			long time = isLong ? BinaryPrimitives.ReadInt64BigEndian(item.Slice(8)) : BinaryPrimitives.ReadInt32BigEndian(item.Slice(4));
			uint rate = BinaryPrimitives.ReadUInt32BigEndian(item.Slice(entrySize - 4));

			if (index is 0 && time is -1)
			{
				empty = duration;
			}
			else if (mediaTime is null && time >= 0 && rate is 0x0001_0000)
			{
				mediaTime = time;
			}
			else
			{
				throw new UnsupportedLayoutException($"track {trackId} has an edit list that cannot be applied to fragments");
			}
		}

		long emptyTicks = movieTimescale is 0 ? 0 : TimeRescale.Near(empty, timescale, movieTimescale);
		return ((mediaTime ?? 0) - emptyTicks, emptyTicks);
	}

	// QuickTime 的声音描述版本 1、2 在固定字段之后还有 16、36 字节
	private static bool IsAac(uint entryType, ReadOnlySpan<byte> entry)
	{
		if (entryType is not BoxType.Mp4a || entry.Length < 28)
		{
			return false;
		}

		int fields = BinaryPrimitives.ReadUInt16BigEndian(entry.Slice(8)) switch
		{
			1 => 44,
			2 => 64,
			_ => 28
		};

		if (entry.Length < fields || !IsoBmff.TryFindBox(entry.Slice(fields), BoxType.Esds, out ReadOnlySpan<byte> esds) || esds.Length < 4)
		{
			return false;
		}

		// ES_Descriptor 的 ES_ID 和标志之后，按标志跳过依赖流、URL 和 OCR 流，接着是 DecoderConfigDescriptor
		ReadOnlySpan<byte> descriptors = esds.Slice(4);

		if (!TryReadDescriptor(ref descriptors, 0x03, out ReadOnlySpan<byte> stream) || stream.Length < 3)
		{
			return false;
		}

		byte flags = stream[2];
		int offset = 3 + ((flags & 0x80) is 0 ? 0 : 2);

		if ((flags & 0x40) is not 0)
		{
			offset += offset < stream.Length ? 1 + stream[offset] : 1;
		}

		offset += (flags & 0x20) is 0 ? 0 : 2;

		if (offset > stream.Length)
		{
			return false;
		}

		ReadOnlySpan<byte> rest = stream.Slice(offset);

		// 0x40 是 MPEG-4 音频，0x66 到 0x68 是 MPEG-2 AAC
		return TryReadDescriptor(ref rest, 0x04, out ReadOnlySpan<byte> config) && config is [0x40 or 0x66 or 0x67 or 0x68, ..];
	}

	// 标签之后的长度每字节 7 位，最高位表示后面还有，最多 4 字节
	private static bool TryReadDescriptor(ref ReadOnlySpan<byte> data, byte tag, out ReadOnlySpan<byte> content)
	{
		content = default;

		if (data is not [var actual, ..] || actual != tag)
		{
			return false;
		}

		int length = 0;
		int offset = 1;
		byte value;

		do
		{
			if (offset > 4 || offset >= data.Length)
			{
				return false;
			}

			value = data[offset++];
			length = length << 7 | value & 0x7F;
		}
		while ((value & 0x80) is not 0);

		if (length > data.Length - offset)
		{
			return false;
		}

		content = data.Slice(offset, length);
		data = data.Slice(offset + length);
		return true;
	}

	// 只有扫描分片才用的轨道信息：tfhd 没给的字段取 trex 的默认值，tfdt 减去 TimeOffset 就是解码时间
	private readonly record struct TrackSource(TrackExtends Defaults, uint DescriptionCount, long TimeOffset);

	private sealed class Scanner(FileWindow window, TrackIndex[] tracks, TrackSource[] sources, Fmp4Track[] timedTracks)
	{
		private readonly Fmp4DecodeTimes _times = new(timedTracks);
		private readonly List<TrackFragmentTime> _fragment = [];
		private readonly List<ByteRange> _mediaRanges = [];

		// 没有 tfdt 的 traf 接着同一轨道上一个 traf 的结尾；第一个从媒体时间 0 开始，和 tfdt 一样要减去 TimeOffset
		private readonly long[] _nextDecodeTimes = [.. sources.Select(static source => -source.TimeOffset)];

		public IReadOnlyList<ByteRange> MediaRanges => _mediaRanges;

		public long TrunSamples { get; private set; }

		// 所有 trun 里样本数据最远的结尾，被丢掉的样本也算
		public long SampleEnd { get; private set; }

		// 返回原始数据的结尾：扫到上次转换追加的盒子就停在它前面；末尾不认识的字节不截掉，转换后留在新 mdat 里
		public long Scan(long position)
		{
			while (position < window.Length && window.ReadHeader(position) is { } header)
			{
				if (IsLeftover(window, header))
				{
					return position;
				}

				if (header.End > window.Length)
				{
					if (header.Type is BoxType.Mdat)
					{
						_mediaRanges.Add(new ByteRange(header.ContentStart, window.Length));
					}

					break;
				}

				if (header.Type is BoxType.Moof)
				{
					ReadMovieFragment(window.ReadBox(header).Slice(header.HeaderSize), header.Start);
				}
				else if (header.Type is BoxType.Mdat)
				{
					_mediaRanges.Add(new ByteRange(header.ContentStart, header.End));
				}

				position = header.End;
			}

			return window.Length;
		}

		private void ReadMovieFragment(ReadOnlySpan<byte> moof, long moofStart)
		{
			_times.ReadMovieFragment(moof, _fragment);

			// 没有 base_data_offset 也不以 moof 为基准的 traf 接着上一个 traf 的数据
			long implicitOffset = moofStart;
			int offset = 0;

			while (IsoBmff.TryReadBox(moof, ref offset, out uint type, out ReadOnlySpan<byte> traf))
			{
				if (type is BoxType.Traf)
				{
					implicitOffset = ReadTrackFragment(traf, offset - traf.Length, moofStart, implicitOffset);
				}
			}
		}

		// fragment 是这个 traf 的样本默认值：tfhd 里有的覆盖 trex 里的
		private long ReadTrackFragment(ReadOnlySpan<byte> traf, int trafStart, long moofStart, long implicitOffset)
		{
			int index = -1;
			TrackExtends fragment = default;
			long baseOffset = implicitOffset;
			long runEnd = implicitOffset;
			long decodeTime = 0;
			int offset = 0;

			while (IsoBmff.TryReadBox(traf, ref offset, out uint type, out ReadOnlySpan<byte> content))
			{
				if (type is BoxType.Tfhd)
				{
					if (content.Length < 8)
					{
						throw new UnsupportedLayoutException("truncated tfhd");
					}

					uint flags = IsoBmff.ReadFlags(content);
					index = FindTrack(BinaryPrimitives.ReadUInt32BigEndian(content.Slice(4)));
					TrackSource source = sources[index];

					if (content.Length < TrackFragmentFlags.ContentLength(flags))
					{
						throw new UnsupportedLayoutException("truncated tfhd");
					}

					int field = 8;
					baseOffset = (flags & TrackFragmentFlags.DefaultBaseIsMoof) is 0 ? implicitOffset : moofStart;

					if ((flags & TrackFragmentFlags.BaseDataOffsetPresent) is not 0)
					{
						baseOffset = (long)Math.Min(BinaryPrimitives.ReadUInt64BigEndian(content.Slice(field)), long.MaxValue / 2);
						field += 8;
					}

					TrackExtends defaults = source.Defaults;
					fragment = defaults with
					{
						DefaultSampleDescriptionIndex = ReadOptional(content, flags, TrackFragmentFlags.SampleDescriptionIndexPresent, ref field, defaults.DefaultSampleDescriptionIndex),
						DefaultSampleDuration = ReadOptional(content, flags, TrackFragmentFlags.DefaultSampleDurationPresent, ref field, defaults.DefaultSampleDuration),
						DefaultSampleSize = ReadOptional(content, flags, TrackFragmentFlags.DefaultSampleSizePresent, ref field, defaults.DefaultSampleSize),
						DefaultSampleFlags = ReadOptional(content, flags, TrackFragmentFlags.DefaultSampleFlagsPresent, ref field, defaults.DefaultSampleFlags)
					};

					if (fragment.DefaultSampleDescriptionIndex is 0 || fragment.DefaultSampleDescriptionIndex > source.DescriptionCount)
					{
						throw new UnsupportedLayoutException($"track {tracks[index].TrackId} refers to sample description {fragment.DefaultSampleDescriptionIndex}");
					}

					runEnd = baseOffset;
					decodeTime = FindDecodeTime(trafStart, traf.Length) is { } time ? time - source.TimeOffset : _nextDecodeTimes[index];
				}
				else if (type is BoxType.Trun)
				{
					if (index < 0)
					{
						throw new UnsupportedLayoutException("trun before tfhd");
					}

					runEnd = ReadTrackRun(content, tracks[index], baseOffset, runEnd, ref decodeTime, fragment);
				}
				else if (type is BoxType.Senc or BoxType.Saiz or BoxType.Saio)
				{
					throw new UnsupportedLayoutException("encrypted samples");
				}
			}

			if (index >= 0)
			{
				_nextDecodeTimes[index] = decodeTime;
			}

			return runEnd;
		}

		// ISO/IEC 14496-12 的 trun：一个 traf 里后面的 trun 的解码时间接着前一个的结尾；没有 data_offset 时数据接着前一个 trun 的数据
		private long ReadTrackRun(ReadOnlySpan<byte> trun, TrackIndex track, long baseOffset, long runEnd, ref long decodeTime, in TrackExtends fragment)
		{
			if (trun.Length < 8)
			{
				throw new UnsupportedLayoutException("truncated trun");
			}

			uint flags = IsoBmff.ReadFlags(trun);
			uint count = BinaryPrimitives.ReadUInt32BigEndian(trun.Slice(4));
			int field = TrackRunFlags.OffsetOf(flags, TrackRunFlags.SampleDurationPresent);

			if (count > MaxSamples - TrunSamples)
			{
				throw new UnsupportedLayoutException($"more than {MaxSamples} samples");
			}

			if (field > trun.Length || count * TrackRunFlags.SampleFieldsSize(flags) > trun.Length - field)
			{
				throw new UnsupportedLayoutException("truncated trun");
			}

			long dataOffset = (flags & TrackRunFlags.DataOffsetPresent) is 0 ? runEnd : baseOffset + BinaryPrimitives.ReadInt32BigEndian(trun.Slice(TrackRunFlags.OffsetOf(flags, TrackRunFlags.DataOffsetPresent)));
			uint firstFlags = (flags & TrackRunFlags.FirstSampleFlagsPresent) is 0 ? fragment.DefaultSampleFlags : BinaryPrimitives.ReadUInt32BigEndian(trun.Slice(TrackRunFlags.OffsetOf(flags, TrackRunFlags.FirstSampleFlagsPresent)));

			if (dataOffset < 0)
			{
				throw new UnsupportedLayoutException($"track {track.TrackId} has data before the file");
			}

			TrunSamples += count;

			for (uint sample = 0; sample < count; ++sample)
			{
				uint duration = ReadOptional(trun, flags, TrackRunFlags.SampleDurationPresent, ref field, fragment.DefaultSampleDuration);
				uint size = ReadOptional(trun, flags, TrackRunFlags.SampleSizePresent, ref field, fragment.DefaultSampleSize);
				uint sampleFlags = ReadOptional(trun, flags, TrackRunFlags.SampleFlagsPresent, ref field, sample is 0 ? firstFlags : fragment.DefaultSampleFlags);

				// 版本 0 的合成偏移按规范是无符号数，超过 2³¹ 的值只会是写入方把负数写进了版本 0，按有符号数读才对
				int compositionOffset = (int)ReadOptional(trun, flags, TrackRunFlags.SampleCompositionTimeOffsetPresent, ref field, 0);

				track.Add(decodeTime, duration, size, sampleFlags, compositionOffset, dataOffset, fragment.DefaultSampleDescriptionIndex, window.Length);
				decodeTime += duration;
				dataOffset += size;
			}

			SampleEnd = Math.Max(SampleEnd, dataOffset);
			return dataOffset;
		}

		private int FindTrack(uint trackId)
		{
			for (int index = 0; index < tracks.Length; ++index)
			{
				if (tracks[index].TrackId == trackId)
				{
					return index;
				}
			}

			throw new UnsupportedLayoutException($"a fragment of unknown track {trackId}");
		}

		// tfdt 已经由 Fmp4DecodeTimes 展开过 32 位回绕
		private long? FindDecodeTime(int trafStart, int trafLength)
		{
			foreach (TrackFragmentTime time in _fragment)
			{
				if (time.TfdtOffset >= trafStart && time.TfdtOffset < trafStart + trafLength)
				{
					return time.DecodeTime > long.MaxValue / 2 ? throw new UnsupportedLayoutException("tfdt out of range") : (long)time.DecodeTime;
				}
			}

			return null;
		}

		private static uint ReadOptional(ReadOnlySpan<byte> box, uint flags, uint field, ref int offset, uint fallback)
		{
			if ((flags & field) is 0)
			{
				return fallback;
			}

			uint value = BinaryPrimitives.ReadUInt32BigEndian(box.Slice(offset));
			offset += 4;
			return value;
		}
	}

	// 顺序扫描时一次读一块，盒子头和 moof 大多落在同一块里
	private sealed class FileWindow(SafeFileHandle handle, long length)
	{
		private byte[] _buffer = new byte[WindowSize];
		private long _start = -1;
		private int _count;

		public long Length => length;

		// 文件不够长时返回的比要的短；每次读 WindowSize 和要的量中较大的那个，窗口为大盒子变大以后，补读盒子头也不会把成段的媒体数据读进来
		public ReadOnlySpan<byte> Read(long position, int count)
		{
			long available = Math.Max(0, length - position);
			int wanted = (int)Math.Min(count, available);

			if (_start < 0 || position < _start || position + wanted > _start + _count)
			{
				if (_buffer.Length < wanted)
				{
					_buffer = new byte[wanted];
				}

				_start = position;
				_count = ReadAt(_buffer.AsSpan(0, (int)Math.Min(Math.Max(wanted, WindowSize), available)), position);
				wanted = Math.Min(wanted, _count);
			}

			return _buffer.AsSpan((int)(position - _start), wanted);
		}

		public BoxHeader? ReadHeader(long position)
		{
			ReadOnlySpan<byte> header = Read(position, LargeBoxHeaderSize);

			if (header.Length < BoxHeaderSize)
			{
				return null;
			}

			long size = BinaryPrimitives.ReadUInt32BigEndian(header);
			int headerSize = BoxHeaderSize;

			if (size is 1)
			{
				if (header.Length < LargeBoxHeaderSize || BinaryPrimitives.ReadUInt64BigEndian(header.Slice(BoxHeaderSize)) is var large && large > long.MaxValue - (ulong)position)
				{
					return null;
				}

				size = (long)large;
				headerSize = LargeBoxHeaderSize;
			}
			else if (size is 0)
			{
				size = length - position;
			}

			return size < headerSize ? null : new BoxHeader(position, size, headerSize, BinaryPrimitives.ReadUInt32BigEndian(header.Slice(4)));
		}

		public bool HasChild(BoxHeader parent, uint type)
		{
			long position = parent.ContentStart;

			while (position < parent.End && ReadHeader(position) is { } child && child.End <= parent.End)
			{
				if (child.Type == type)
				{
					return true;
				}

				position = child.End;
			}

			return false;
		}

		public ReadOnlySpan<byte> ReadBox(BoxHeader header)
		{
			ReadOnlySpan<byte> box = Read(header.Start, GetReadSize(header));
			return box.Length == header.Size ? box : throw new UnsupportedLayoutException($"truncated box at {header.Start}");
		}

		// 读进单独的数组，不经过窗口：内容要一直留着用，窗口也不用为它变大
		public byte[] ReadContent(BoxHeader header)
		{
			byte[] content = new byte[GetReadSize(header) - header.HeaderSize];
			return ReadAt(content, header.ContentStart) == content.Length ? content : throw new UnsupportedLayoutException($"truncated box at {header.Start}");
		}

		private static int GetReadSize(BoxHeader header)
		{
			return header.Size <= MaxBoxReadSize ? (int)header.Size : throw new UnsupportedLayoutException($"a {header.Size}-byte box at {header.Start}");
		}

		private int ReadAt(Span<byte> buffer, long position)
		{
			int read = 0;

			while (read < buffer.Length)
			{
				int chunk = RandomAccess.Read(handle, buffer.Slice(read), position + read);

				if (chunk is 0)
				{
					break;
				}

				read += chunk;
			}

			return read;
		}
	}
}
