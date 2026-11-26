using Microsoft.Win32.SafeHandles;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace BilibiliLiveRecordDownLoader.Application.Recording.Hybrid;

// ChapterData 是章节文本样本所在的范围；新 moov 先以 free 的类型写出
internal readonly record struct AppendedMovie(ByteRange ChapterData, long MovieStart, long MovieSize, int ChapterCount)
{
	public long End => MovieStart + MovieSize;
}

// 在文件末尾写 [放章节文本的 mdat][free，内容是新 moov]，样本表直接指向原来分片里的数据
internal static class HybridMovieWriter
{
	// 章节文本样本项按 3GPP 定时文本的格式给出默认值：显示标志、对齐、背景色、默认文本框、样式记录和只有一个空字体名的字体表
	private static ReadOnlySpan<byte> TextSampleEntry =>
	[
		0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
		0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
		0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
		0x00, 0x00, 0x00, 0x0D, (byte)'f', (byte)'t', (byte)'a', (byte)'b', 0x00, 0x01, 0x00, 0x01, 0x00
	];

	private static ReadOnlySpan<byte> IdentityMatrix =>
	[
		0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
		0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
		0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00
	];

	public static AppendedMovie Write(SafeFileHandle handle, FragmentedLayout layout, IReadOnlyList<MediaChapter> chapters, ReadOnlySpan<byte> header)
	{
		(uint movieTimescale, TrackTiming[] timings) = MovieTiming.Compute(layout.Tracks);
		uint lastTrackId = layout.Tracks.Max(static track => track.TrackId);
		ChapterTrack? chapterTrack = HybridChapters.Create(chapters, movieTimescale, lastTrackId + 1);
		ReadOnlySpan<byte> movie = layout.MovieContent.Span;
		long duration = Math.Max(timings.Max(static timing => timing.Duration), chapterTrack?.End ?? 0);
		uint[] trackIds = [.. timings.Select(static timing => timing.Track.TrackId)];

		using BoxWriter writer = new(handle, layout.DataEnd);
		ByteRange chapterData = chapterTrack is null ? new ByteRange(writer.Position, writer.Position) : WriteChapterData(writer, chapterTrack.Samples);
		long movieStart = writer.Position;

		writer.Begin(BoxType.Free);
		WriteMovieHeader(writer, FindBox(movie, BoxType.Mvhd), movieTimescale, duration, (chapterTrack?.TrackId ?? lastTrackId) + 1);

		foreach (TrackTiming timing in timings)
		{
			WriteTrack(writer, timing, trackIds, chapterTrack?.TrackId);
		}

		if (chapterTrack is not null)
		{
			WriteChapterTrack(writer, chapterTrack, chapterData.Start);
		}

		WriteUserData(writer, movie, chapterTrack, header, layout.DataEnd);

		foreach (Box box in new BoxEnumerator(movie))
		{
			if (box.Type is not (BoxType.Mvhd or BoxType.Trak or BoxType.Mvex or BoxType.Udta))
			{
				writer.Write(box.Bytes);
			}
		}

		writer.End();
		writer.Flush();

		return new AppendedMovie(chapterData, movieStart, writer.Position - movieStart, chapterTrack?.Samples.Length ?? 0);
	}

	private static ByteRange WriteChapterData(BoxWriter writer, ChapterSample[] samples)
	{
		writer.Begin(BoxType.Mdat);
		writer.Write(HybridMovie.Signature);
		long start = writer.Position;

		foreach (ChapterSample sample in samples)
		{
			writer.Write(sample.Data);
		}

		ByteRange data = new(start, writer.Position);
		writer.End();
		return data;
	}

	// 修改时间和时长之间是时间刻度；时长之后依次是速率、音量、保留字段、矩阵、预定义字段和 next_track_ID
	private static void WriteMovieHeader(BoxWriter writer, ReadOnlySpan<byte> mvhd, uint movieTimescale, long duration, uint minimumNextTrackId)
	{
		int rest = mvhd is [1, ..] ? 32 : 20;

		if (mvhd.Length < rest + 80)
		{
			throw new UnsupportedLayoutException("truncated mvhd");
		}

		(ulong creation, ulong modification) = ReadTimes(mvhd);
		bool isLong = BeginHeader(writer, BoxType.Mvhd, IsoBmff.ReadFlags(mvhd), creation, modification, duration);
		writer.WriteUInt32(movieTimescale);
		writer.WriteUInt32Or64((ulong)duration, isLong);
		writer.Write(mvhd.Slice(rest, 76));
		writer.WriteUInt32(Math.Max(BinaryPrimitives.ReadUInt32BigEndian(mvhd.Slice(rest + 76)), minimumNextTrackId));
		writer.End();
	}

	private static void WriteTrack(BoxWriter writer, TrackTiming timing, ReadOnlySpan<uint> trackIds, uint? chapterTrackId)
	{
		TrackIndex track = timing.Track;
		ReadOnlySpan<byte> trak = track.Trak;

		writer.Begin(BoxType.Trak);

		// 修改时间和时长之间是 track_ID 和保留字段，时长之后是保留字段、层、备用组、音量、矩阵和宽高
		CopyHeader(writer, BoxType.Tkhd, FindBox(trak, BoxType.Tkhd), 8, 60, timing.Duration);
		WriteEditList(writer, timing.Edits);
		WriteTrackReferences(writer, trak, trackIds, chapterTrackId);
		writer.Begin(BoxType.Mdia);

		foreach (Box box in new BoxEnumerator(FindBox(trak, BoxType.Mdia)))
		{
			switch (box.Type)
			{
				case BoxType.Mdhd:
					// 修改时间和时长之间是时间刻度，时长之后是语言和预定义字段
					CopyHeader(writer, BoxType.Mdhd, box.Content, 4, 4, track.MediaDuration);
					break;
				case BoxType.Minf:
					writer.Begin(BoxType.Minf);

					foreach (Box child in new BoxEnumerator(box.Content))
					{
						if (child.Type is BoxType.Stbl)
						{
							WriteSampleTable(writer, track, child.Content);
						}
						else
						{
							writer.Write(child.Bytes);
						}
					}

					writer.End();
					break;
				default:
					writer.Write(box.Bytes);
					break;
			}
		}

		writer.End();

		foreach (Box box in new BoxEnumerator(trak))
		{
			if (box.Type is not (BoxType.Tkhd or BoxType.Edts or BoxType.Tref or BoxType.Mdia))
			{
				writer.Write(box.Bytes);
			}
		}

		writer.End();
	}

	// 没有样本的轨道不写出，源轨道的引用只留指向写出轨道的 ID；有章节时新的章节轨代替原来的章节引用
	private static void WriteTrackReferences(BoxWriter writer, ReadOnlySpan<byte> trak, ReadOnlySpan<uint> trackIds, uint? chapterTrackId)
	{
		bool hasReferences = chapterTrackId is not null;

		if (IsoBmff.TryFindBox(trak, BoxType.Tref, out ReadOnlySpan<byte> references))
		{
			foreach (Box reference in new BoxEnumerator(references))
			{
				hasReferences |= IsKept(reference, trackIds, chapterTrackId);
			}
		}

		if (!hasReferences)
		{
			return;
		}

		writer.Begin(BoxType.Tref);

		foreach (Box reference in new BoxEnumerator(references))
		{
			if (!IsKept(reference, trackIds, chapterTrackId))
			{
				continue;
			}

			writer.Begin(reference.Type);

			for (ReadOnlySpan<byte> ids = reference.Content; ids.Length >= sizeof(uint); ids = ids.Slice(sizeof(uint)))
			{
				uint id = BinaryPrimitives.ReadUInt32BigEndian(ids);

				if (trackIds.Contains(id))
				{
					writer.WriteUInt32(id);
				}
			}

			writer.End();
		}

		if (chapterTrackId is { } chapter)
		{
			writer.Begin(BoxType.Chap);
			writer.WriteUInt32(chapter);
			writer.End();
		}

		writer.End();
	}

	private static bool IsKept(Box reference, ReadOnlySpan<uint> trackIds, uint? chapterTrackId)
	{
		if (chapterTrackId is not null && reference.Type is BoxType.Chap)
		{
			return false;
		}

		for (ReadOnlySpan<byte> ids = reference.Content; ids.Length >= sizeof(uint); ids = ids.Slice(sizeof(uint)))
		{
			if (trackIds.Contains(BinaryPrimitives.ReadUInt32BigEndian(ids)))
			{
				return true;
			}
		}

		return false;
	}

	private static void WriteEditList(BoxWriter writer, EditEntry[] edits)
	{
		bool isLong = edits.Any(static edit => edit.Duration >= int.MaxValue || edit.MediaTime >= int.MaxValue);

		writer.Begin(BoxType.Edts);
		writer.BeginFull(BoxType.Elst, isLong ? (byte)1 : (byte)0);
		writer.WriteUInt32((uint)edits.Length);

		foreach ((long duration, long mediaTime) in edits)
		{
			writer.WriteUInt32Or64((ulong)duration, isLong);
			writer.WriteUInt32Or64((ulong)mediaTime, isLong);

			// 播放速率 1.0
			writer.WriteUInt32(0x0001_0000);
		}

		writer.End();
		writer.End();
	}

	private static void WriteSampleTable(BoxWriter writer, TrackIndex track, ReadOnlySpan<byte> stbl)
	{
		writer.Begin(BoxType.Stbl);

		writer.Begin(BoxType.Stsd);
		writer.Write(FindBox(stbl, BoxType.Stsd));
		writer.End();

		writer.BeginFull(BoxType.Stts, 0);
		writer.WriteUInt32((uint)track.TimeToSample.Count);

		// 表项的样本数和时长在内存里依次排列，和 stts 里的一样，整块当作 uint 转成大端
		foreach (ReadOnlyMemory<TimeToSample> chunk in track.TimeToSample.EnumerateChunks())
		{
			writer.WriteUInt32s(MemoryMarshal.Cast<TimeToSample, uint>(chunk.Span));
		}

		writer.End();

		if (track.Kind is TrackKind.Video && track.SyncSamples.Count < track.SampleCount)
		{
			writer.BeginFull(BoxType.Stss, 0);
			writer.WriteUInt32((uint)track.SyncSamples.Count);
			WriteAll(writer, track.SyncSamples);
			writer.End();
		}

		WriteCompositionOffsets(writer, track.CompositionOffsets, track.CompositionShift);
		WriteChunks(writer, track.Chunks);
		WriteSampleSizes(writer, track.Sizes);
		WriteChunkOffsets(writer, track.Chunks);

		// AAC 每帧解出的声音要和前一帧的重叠相加，从中间开始播放时要先解前一帧，否则开头有杂音；用 roll 样本组把每个样本标成预滚动一帧
		if (track.IsAac)
		{
			writer.BeginFull(BoxType.Sgpd, 1);
			writer.WriteUInt32(BoxType.Roll);
			writer.WriteUInt32(2);
			writer.WriteUInt32(1);
			writer.WriteUInt16(unchecked((ushort)-1));
			writer.End();

			writer.BeginFull(BoxType.Sbgp, 0);
			writer.WriteUInt32(BoxType.Roll);
			writer.WriteUInt32(1);
			writer.WriteUInt32((uint)track.SampleCount);
			writer.WriteUInt32(1);
			writer.End();
		}

		writer.End();
	}

	// 没有 ctts 时显示时间等于解码时间；负的偏移要用规范后来才加的版本 1，加上 shift 都不是负数后用最早的版本 0，读取方都认
	private static void WriteCompositionOffsets(BoxWriter writer, ChunkedList<int> offsets, int shift)
	{
		int current = offsets[0];

		if (current == -shift && AllEqual(offsets, current))
		{
			return;
		}

		writer.BeginFull(BoxType.Ctts, 0);
		long entryCount = writer.ReserveUInt32();
		uint entries = 1;
		uint count = 0;

		foreach (ReadOnlyMemory<int> chunk in offsets.EnumerateChunks())
		{
			foreach (int offset in chunk.Span)
			{
				if (offset != current)
				{
					writer.WriteUInt32(count);
					writer.WriteUInt32((uint)(current + (long)shift));
					current = offset;
					count = 0;
					++entries;
				}

				++count;
			}
		}

		writer.WriteUInt32(count);
		writer.WriteUInt32((uint)(current + (long)shift));
		writer.PatchUInt32(entryCount, entries);
		writer.End();
	}

	private static void WriteChunks(BoxWriter writer, IReadOnlyList<SampleChunk> chunks)
	{
		writer.BeginFull(BoxType.Stsc, 0);
		long entryCount = writer.ReserveUInt32();
		uint entries = 0;
		(uint Count, uint DescriptionIndex) current = default;

		for (int index = 0; index < chunks.Count; ++index)
		{
			SampleChunk chunk = chunks[index];

			if (index is 0 || (chunk.Count, chunk.DescriptionIndex) != current)
			{
				current = (chunk.Count, chunk.DescriptionIndex);
				writer.WriteUInt32((uint)index + 1);
				writer.WriteUInt32(chunk.Count);
				writer.WriteUInt32(chunk.DescriptionIndex);
				++entries;
			}
		}

		writer.PatchUInt32(entryCount, entries);
		writer.End();
	}

	// sample_size 为 0 表示后面跟着逐个样本的大小表，所以样本全是 0 字节时也要写表
	private static void WriteSampleSizes(BoxWriter writer, ChunkedList<uint> sizes)
	{
		uint first = sizes[0];
		bool isUniform = first is not 0 && AllEqual(sizes, first);

		writer.BeginFull(BoxType.Stsz, 0);
		writer.WriteUInt32(isUniform ? first : 0);
		writer.WriteUInt32((uint)sizes.Count);

		if (!isUniform)
		{
			WriteAll(writer, sizes);
		}

		writer.End();
	}

	// 偏移超过 32 位时整张表改用 co64
	private static void WriteChunkOffsets(BoxWriter writer, IReadOnlyList<SampleChunk> chunks)
	{
		bool isLong = chunks.Any(static chunk => chunk.Offset > uint.MaxValue);

		writer.BeginFull(isLong ? BoxType.Co64 : BoxType.Stco, 0);
		writer.WriteUInt32((uint)chunks.Count);

		for (int index = 0; index < chunks.Count; ++index)
		{
			writer.WriteUInt32Or64((ulong)chunks[index].Offset, isLong);
		}

		writer.End();
	}

	private static void WriteChapterTrack(BoxWriter writer, ChapterTrack track, long dataStart)
	{
		ChapterSample[] samples = track.Samples;
		long mediaDuration = samples.Sum(static sample => sample.Duration);
		SampleChunk[] chunk = [new(dataStart, (uint)samples.Length, 1)];

		writer.Begin(BoxType.Trak);

		// 只在电影里，不启用：播放器不把它当字幕轨显示
		bool isLong = BeginHeader(writer, BoxType.Tkhd, 0x02, 0, 0, track.End);
		writer.WriteUInt32(track.TrackId);
		writer.WriteZeros(4);
		writer.WriteUInt32Or64((ulong)track.End, isLong);
		writer.WriteZeros(16);
		writer.Write(IdentityMatrix);
		writer.WriteZeros(8);
		writer.End();

		WriteEditList(writer, track.Start > 0 ? [new EditEntry(track.Start, -1), new EditEntry(track.End - track.Start, 0)] : [new EditEntry(track.End, 0)]);

		writer.Begin(BoxType.Mdia);

		isLong = BeginHeader(writer, BoxType.Mdhd, 0, 0, 0, mediaDuration);
		writer.WriteUInt32(HybridChapters.Timescale);
		writer.WriteUInt32Or64((ulong)mediaDuration, isLong);
		writer.WriteUInt32(0);
		writer.End();

		writer.BeginFull(BoxType.Hdlr, 0);
		writer.WriteUInt32(0);
		writer.WriteUInt32(BoxType.Text);
		writer.WriteZeros(12);
		writer.Write("SubtitleHandler\0"u8);
		writer.End();

		writer.Begin(BoxType.Minf);
		writer.Begin(BoxType.Gmhd);
		writer.BeginFull(BoxType.Gmin, 0);
		writer.WriteUInt16(0x0040);
		writer.WriteUInt16(0x8000);
		writer.WriteUInt16(0x8000);
		writer.WriteUInt16(0x8000);
		writer.WriteUInt32(0);
		writer.End();

		// QuickTime 文本轨的 gmhd 里 text 盒子的内容也是单位矩阵
		writer.Begin(BoxType.Text);
		writer.Write(IdentityMatrix);
		writer.End();
		writer.End();

		writer.Begin(BoxType.Dinf);
		writer.BeginFull(BoxType.Dref, 0);
		writer.WriteUInt32(1);
		writer.BeginFull(BoxType.Url, 0, 0x01);
		writer.End();
		writer.End();
		writer.End();

		writer.Begin(BoxType.Stbl);
		writer.BeginFull(BoxType.Stsd, 0);
		writer.WriteUInt32(1);
		writer.Begin(BoxType.Text);
		writer.WriteZeros(6);
		writer.WriteUInt16(1);
		writer.Write(TextSampleEntry);
		writer.End();
		writer.End();

		writer.BeginFull(BoxType.Stts, 0);
		writer.WriteUInt32((uint)samples.Length);

		foreach (ChapterSample sample in samples)
		{
			writer.WriteUInt32(1);
			writer.WriteUInt32(sample.Duration);
		}

		writer.End();

		WriteChunks(writer, chunk);

		writer.BeginFull(BoxType.Stsz, 0);
		writer.WriteUInt32(0);
		writer.WriteUInt32((uint)samples.Length);

		foreach (ChapterSample sample in samples)
		{
			writer.WriteUInt32((uint)sample.Data.Length);
		}

		writer.End();

		WriteChunkOffsets(writer, chunk);
		writer.End();
		writer.End();
		writer.End();
		writer.End();
	}

	// 头部备份：转换时覆盖掉的开头原始字节和原文件长度，留着以后把文件还原成录下来的样子
	private static void WriteUserData(BoxWriter writer, ReadOnlySpan<byte> movie, ChapterTrack? chapterTrack, ReadOnlySpan<byte> header, long originalLength)
	{
		writer.Begin(BoxType.Udta);

		// 源里的 chpl 被新的章节代替，否则会有两个
		if (IsoBmff.TryFindBox(movie, BoxType.Udta, out ReadOnlySpan<byte> udta))
		{
			foreach (Box box in new BoxEnumerator(udta))
			{
				if (chapterTrack is null || box.Type is not BoxType.Chpl)
				{
					writer.Write(box.Bytes);
				}
			}
		}

		if (chapterTrack is not null)
		{
			HybridChapters.WriteNeroChapters(writer, chapterTrack.Samples);
		}

		writer.Begin(BoxType.Uuid);
		writer.Write(HybridMovie.Signature);
		writer.WriteUInt64((ulong)originalLength);
		writer.Write(header);
		writer.End();

		writer.End();
	}

	// mvhd、tkhd、mdhd 都以创建时间、修改时间开头，隔几个字段后是时长，版本 1 的这三个字段是 64 位；时长到 int.MaxValue 就用版本 1
	// 写到修改时间为止，返回是否用了版本 1
	private static bool BeginHeader(BoxWriter writer, uint type, uint flags, ulong creation, ulong modification, long duration)
	{
		bool isLong = duration >= int.MaxValue || creation > uint.MaxValue || modification > uint.MaxValue;
		writer.BeginFull(type, isLong ? (byte)1 : (byte)0, flags);
		writer.WriteUInt32Or64(creation, isLong);
		writer.WriteUInt32Or64(modification, isLong);
		return isLong;
	}

	// 修改时间和时长之间的 middleLength 字节、时长之后的 restLength 字节两个版本相同，原样复制
	private static void CopyHeader(BoxWriter writer, uint type, ReadOnlySpan<byte> source, int middleLength, int restLength, long duration)
	{
		int middle = source is [1, ..] ? 20 : 12;
		int rest = middle + middleLength + (source is [1, ..] ? 8 : 4);

		if (source.Length < rest + restLength)
		{
			throw new UnsupportedLayoutException($"truncated {BoxType.GetName(type)}");
		}

		(ulong creation, ulong modification) = ReadTimes(source);
		bool isLong = BeginHeader(writer, type, IsoBmff.ReadFlags(source), creation, modification, duration);
		writer.Write(source.Slice(middle, middleLength));
		writer.WriteUInt32Or64((ulong)duration, isLong);
		writer.Write(source.Slice(rest, restLength));
		writer.End();
	}

	private static (ulong Creation, ulong Modification) ReadTimes(ReadOnlySpan<byte> fullBox)
	{
		return fullBox is [1, ..]
			? (BinaryPrimitives.ReadUInt64BigEndian(fullBox.Slice(4)), BinaryPrimitives.ReadUInt64BigEndian(fullBox.Slice(12)))
			: (BinaryPrimitives.ReadUInt32BigEndian(fullBox.Slice(4)), BinaryPrimitives.ReadUInt32BigEndian(fullBox.Slice(8)));
	}

	private static void WriteAll(BoxWriter writer, ChunkedList<uint> values)
	{
		foreach (ReadOnlyMemory<uint> chunk in values.EnumerateChunks())
		{
			writer.WriteUInt32s(chunk.Span);
		}
	}

	private static bool AllEqual<T>(ChunkedList<T> values, T value) where T : unmanaged, IEquatable<T>
	{
		foreach (ReadOnlyMemory<T> chunk in values.EnumerateChunks())
		{
			if (chunk.Span.ContainsAnyExcept(value))
			{
				return false;
			}
		}

		return true;
	}

	private static ReadOnlySpan<byte> FindBox(ReadOnlySpan<byte> data, uint type)
	{
		return IsoBmff.TryFindBox(data, type, out ReadOnlySpan<byte> content) ? content : throw new UnsupportedLayoutException($"no {BoxType.GetName(type)}");
	}

	// Bytes 是整个盒子，包括盒子头
	private readonly ref struct Box(uint type, ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> content)
	{
		public uint Type { get; } = type;

		public ReadOnlySpan<byte> Bytes { get; } = bytes;

		public ReadOnlySpan<byte> Content { get; } = content;
	}

	private ref struct BoxEnumerator(ReadOnlySpan<byte> data)
	{
		private readonly ReadOnlySpan<byte> _data = data;
		private int _offset;

		public Box Current { get; private set; }

		public readonly BoxEnumerator GetEnumerator()
		{
			return this;
		}

		public bool MoveNext()
		{
			int start = _offset;

			if (!IsoBmff.TryReadBox(_data, ref _offset, out uint type, out ReadOnlySpan<byte> content))
			{
				return false;
			}

			Current = new Box(type, _data.Slice(start, _offset - start), content);
			return true;
		}
	}
}
