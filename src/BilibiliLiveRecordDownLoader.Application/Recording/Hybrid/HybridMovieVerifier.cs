using Microsoft.Win32.SafeHandles;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;

namespace BilibiliLiveRecordDownLoader.Application.Recording.Hybrid;

// 改文件开头之前，把刚写出的 moov 从磁盘读回来核对，不用写入时内存里的数据：
// 能解析；每个样本都落在原来分片的 mdat 数据里（章节样本落在章节 mdat 里）；样本数等于所有 trun 的样本数减去有意丢掉的，往回走的样本只改时间，照样计数；每条轨道 stts 的时长加起来等于 mdhd 的时长
// 样本表按顺序流式读，十小时的录制也不用把几十 MB 的 moov 整个读进内存
internal static class HybridMovieVerifier
{
	public static string? Verify(SafeFileHandle handle, FragmentedLayout layout, AppendedMovie appended)
	{
		long expectedSamples = layout.TrunSamples - layout.DroppedSamples;
		int expectedTracks = layout.Tracks.Count(static track => track.SampleCount > 0);

		using TableReaders readers = new(handle);
		RegionReader reader = readers.Boxes;

		if (ReadHeader(reader, appended.MovieStart, appended.End) is not { Type: BoxType.Free } movie || movie.End != appended.End)
		{
			return "the new moov cannot be read back";
		}

		bool hasMovieHeader = false;
		long mediaSamples = 0;
		long chapterSamples = 0;
		int mediaTracks = 0;
		long position = movie.ContentStart;

		while (position < movie.End)
		{
			if (ReadHeader(reader, position, movie.End) is not { } box)
			{
				return $"a malformed box at {position}";
			}

			if (box.Type is BoxType.Mvhd)
			{
				hasMovieHeader = true;
			}
			else if (box.Type is BoxType.Trak)
			{
				switch (VerifyTrack(box, layout.MediaRanges, appended.ChapterData, readers))
				{
					case TrackCheck.Failed { Reason: var reason }:
						return reason;
					case TrackCheck.Media { Samples: var samples }:
						mediaSamples += samples;
						++mediaTracks;
						break;
					case TrackCheck.Chapters { Samples: var samples }:
						chapterSamples += samples;
						break;
					default:
						throw new UnreachableException();
				}
			}

			position = box.End;
		}

		if (!hasMovieHeader)
		{
			return "no mvhd";
		}

		if (mediaTracks != expectedTracks)
		{
			return $"{mediaTracks} tracks instead of {expectedTracks}";
		}

		if (mediaSamples != expectedSamples)
		{
			return $"{mediaSamples} samples instead of {expectedSamples}";
		}

		return chapterSamples != appended.ChapterCount ? $"{chapterSamples} chapters instead of {appended.ChapterCount}" : null;
	}

	// 大小检查都按盒子内容算：full box 的内容先是 4 字节版本和标志，表的开头是 4 字节表项数
	private static TrackCheck VerifyTrack(BoxHeader trak, IReadOnlyList<ByteRange> mediaRanges, ByteRange chapterData, TableReaders readers)
	{
		RegionReader reader = readers.Boxes;

		if (FindChild(reader, trak, BoxType.Mdia) is not { } mdia || FindChild(reader, mdia, BoxType.Mdhd) is not { } mdhd || FindChild(reader, mdia, BoxType.Hdlr) is not { ContentSize: >= 12 } hdlr || FindChild(reader, mdia, BoxType.Minf) is not { } minf || FindChild(reader, minf, BoxType.Stbl) is not { } stbl)
		{
			return new TrackCheck.Failed("a track without mdhd, hdlr or stbl");
		}

		if (ReadMediaDuration(reader, mdhd) is not { } mediaDuration)
		{
			return new TrackCheck.Failed("truncated mdhd");
		}

		reader.Position = hdlr.ContentStart + 8;
		bool isText = reader.ReadUInt32() is BoxType.Text;

		// 样本表很大，只走一遍子盒子头，不为每个子盒子从头找
		BoxHeader? descriptions = null, timeToSample = null, compositionOffsets = null, syncSamples = null, sampleToChunk = null, sampleSizes = null, chunkOffsets = null;

		for (long position = stbl.ContentStart; ReadHeader(reader, position, stbl.End) is { } child; position = child.End)
		{
			switch (child.Type)
			{
				case BoxType.Stsd:
					descriptions ??= child;
					break;
				case BoxType.Stts:
					timeToSample ??= child;
					break;
				case BoxType.Ctts:
					compositionOffsets ??= child;
					break;
				case BoxType.Stss:
					syncSamples ??= child;
					break;
				case BoxType.Stsc:
					sampleToChunk ??= child;
					break;
				case BoxType.Stsz:
					sampleSizes ??= child;
					break;
				case BoxType.Stco or BoxType.Co64:
					chunkOffsets ??= child;
					break;
			}
		}

		if (descriptions is not { ContentSize: >= 8 } stsd || timeToSample is not { ContentSize: >= 8 } stts || sampleToChunk is not { ContentSize: >= 8 } stsc || sampleSizes is not { ContentSize: >= 12 } stsz)
		{
			return new TrackCheck.Failed("a sample table without stsd, stts, stsc or stsz");
		}

		if (chunkOffsets is not { ContentSize: >= 8 } offsetTable)
		{
			return new TrackCheck.Failed("a sample table without chunk offsets");
		}

		bool isLongOffsets = offsetTable.Type is BoxType.Co64;
		reader.Position = stsd.ContentStart + 4;
		uint descriptionCount = reader.ReadUInt32();
		reader.Position = stsz.ContentStart + 4;
		uint uniformSize = reader.ReadUInt32();
		uint sampleCount = reader.ReadUInt32();

		if (uniformSize is 0 && stsz.ContentSize < 12 + 4L * sampleCount)
		{
			return new TrackCheck.Failed("truncated stsz");
		}

		if (SumEntries(reader, stts) is not (var timedSamples, var duration) || timedSamples != sampleCount)
		{
			return new TrackCheck.Failed("stts does not cover every sample");
		}

		if (duration != mediaDuration)
		{
			return new TrackCheck.Failed($"stts adds up to {duration} instead of the mdhd duration {mediaDuration}");
		}

		if (compositionOffsets is { } ctts && SumEntries(reader, ctts)?.Samples != sampleCount)
		{
			return new TrackCheck.Failed("ctts does not cover every sample");
		}

		if (syncSamples is { } stss && !AreSyncSamplesValid(reader, stss, sampleCount))
		{
			return new TrackCheck.Failed("invalid stss");
		}

		reader.Position = offsetTable.ContentStart + 4;
		uint chunkCount = reader.ReadUInt32();
		reader.Position = stsc.ContentStart + 4;
		uint entryCount = reader.ReadUInt32();

		if (offsetTable.ContentSize < 8 + (isLongOffsets ? 8L : 4L) * chunkCount || stsc.ContentSize < 8 + 12L * entryCount)
		{
			return new TrackCheck.Failed("truncated stsc or chunk offsets");
		}

		IReadOnlyList<ByteRange> ranges = isText ? [chapterData] : mediaRanges;
		RegionReader sizes = readers.Sizes;
		RegionReader offsets = readers.Offsets;
		RegionReader entries = readers.Entries;
		sizes.Position = stsz.ContentStart + 12;
		offsets.Position = offsetTable.ContentStart + 8;
		entries.Position = stsc.ContentStart + 8;
		uint chunk = 0;
		long samples = 0;

		for (uint entry = 0; entry < entryCount; ++entry)
		{
			uint firstChunk = entries.ReadUInt32();
			uint samplesPerChunk = entries.ReadUInt32();
			uint descriptionIndex = entries.ReadUInt32();
			uint nextFirstChunk = entry + 1 < entryCount ? entries.PeekUInt32() : chunkCount + 1;

			if (firstChunk != chunk + 1 || nextFirstChunk <= firstChunk || nextFirstChunk > chunkCount + 1 || samplesPerChunk is 0 || descriptionIndex < 1 || descriptionIndex > descriptionCount)
			{
				return new TrackCheck.Failed("invalid stsc");
			}

			for (; chunk + 1 < nextFirstChunk; ++chunk)
			{
				long start = isLongOffsets ? (long)Math.Min(offsets.ReadUInt64(), long.MaxValue / 2) : offsets.ReadUInt32();
				long end = start;

				if (samples + samplesPerChunk > sampleCount)
				{
					return new TrackCheck.Failed("stsc describes more samples than stsz");
				}

				for (uint sample = 0; sample < samplesPerChunk; ++sample)
				{
					end += uniformSize is 0 ? sizes.ReadUInt32() : uniformSize;
				}

				samples += samplesPerChunk;

				if (!IsInside(ranges, start, end))
				{
					return new TrackCheck.Failed($"samples at {start}..{end} are outside the media data");
				}
			}
		}

		if (chunk != chunkCount || samples != sampleCount)
		{
			return new TrackCheck.Failed("stsc does not cover every chunk and sample");
		}

		return isText ? new TrackCheck.Chapters(samples) : new TrackCheck.Media(samples);
	}

	// 范围按位置排好、互不重叠，只有第一个结尾不早于 end 的范围可能盖住 [start, end)；只有 0 字节样本的块恰好从某个 mdat 数据的结尾开始时，也算在它里面
	private static bool IsInside(IReadOnlyList<ByteRange> ranges, long start, long end)
	{
		int low = 0;
		int high = ranges.Count - 1;

		while (low <= high)
		{
			int middle = low + (high - low) / 2;

			if (ranges[middle].End < end)
			{
				low = middle + 1;
			}
			else
			{
				high = middle - 1;
			}
		}

		return low < ranges.Count && ranges[low].Start <= start;
	}

	// 版本 1 的创建时间、修改时间和时长是 64 位，时间刻度在修改时间之后
	private static ulong? ReadMediaDuration(RegionReader reader, BoxHeader mdhd)
	{
		reader.Position = mdhd.ContentStart;
		bool isLong = reader.ReadUInt32() >> 24 is 1;

		if (mdhd.ContentSize < (isLong ? 32 : 20))
		{
			return null;
		}

		reader.Position = mdhd.ContentStart + (isLong ? 24 : 16);
		return isLong ? reader.ReadUInt64() : reader.ReadUInt32();
	}

	// stts、ctts 的表项是 8 字节：样本数，然后是这些样本各自的时长或合成偏移；Total 是每项样本数乘第二个字段之和，只对 stts 有意义，就是媒体时长；样本数之和等于 stsz 的样本数时 Total 不会溢出
	private static (long Samples, ulong Total)? SumEntries(RegionReader reader, BoxHeader box)
	{
		reader.Position = box.ContentStart + 4;
		uint count = reader.ReadUInt32();

		if (box.ContentSize < 8 + 8L * count)
		{
			return null;
		}

		long samples = 0;
		ulong total = 0;

		for (uint entry = 0; entry < count; ++entry)
		{
			uint entrySamples = reader.ReadUInt32();
			samples += entrySamples;
			total += (ulong)entrySamples * reader.ReadUInt32();
		}

		return (samples, total);
	}

	private static bool AreSyncSamplesValid(RegionReader reader, BoxHeader stss, uint sampleCount)
	{
		reader.Position = stss.ContentStart + 4;
		uint count = reader.ReadUInt32();

		if (stss.ContentSize < 8 + 4L * count)
		{
			return false;
		}

		uint previous = 0;

		for (uint entry = 0; entry < count; ++entry)
		{
			uint sample = reader.ReadUInt32();

			if (sample <= previous || sample > sampleCount)
			{
				return false;
			}

			previous = sample;
		}

		return true;
	}

	private static BoxHeader? FindChild(RegionReader reader, BoxHeader parent, uint type)
	{
		for (long position = parent.ContentStart; ReadHeader(reader, position, parent.End) is { } child; position = child.End)
		{
			if (child.Type == type)
			{
				return child;
			}
		}

		return null;
	}

	private static BoxHeader? ReadHeader(RegionReader reader, long position, long limit)
	{
		if (limit - position < 8)
		{
			return null;
		}

		reader.Position = position;
		long size = reader.ReadUInt32();
		uint type = reader.ReadUInt32();
		int headerSize = 8;

		if (size is 1)
		{
			if (limit - position < 16)
			{
				return null;
			}

			size = (long)Math.Min(reader.ReadUInt64(), long.MaxValue / 2);
			headerSize = 16;
		}

		return size < headerSize || size > limit - position ? null : new BoxHeader(position, size, headerSize, type);
	}

	private closed record TrackCheck
	{
		public sealed record Media(long Samples) : TrackCheck;

		public sealed record Chapters(long Samples) : TrackCheck;

		public sealed record Failed(string Reason) : TrackCheck;
	}

	// 盒子头和三张并行读的表各用一个读取器，各自顺序前进；文件在校验期间不会变，每条轨道接着用
	private sealed class TableReaders(SafeFileHandle handle) : IDisposable
	{
		public RegionReader Boxes { get; } = new(handle);

		public RegionReader Sizes { get; } = new(handle);

		public RegionReader Offsets { get; } = new(handle);

		public RegionReader Entries { get; } = new(handle);

		public void Dispose()
		{
			Boxes.Dispose();
			Sizes.Dispose();
			Offsets.Dispose();
			Entries.Dispose();
		}
	}

	// 顺序读时一次读一块；读到文件末尾之外按 0 处理，由调用方用盒子大小把关
	private sealed class RegionReader(SafeFileHandle handle) : IDisposable
	{
		private const int BufferSize = 1 << 16;

		private readonly byte[] _buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
		private long _bufferStart;
		private int _count;

		public long Position { get; set; }

		public uint ReadUInt32()
		{
			uint value = BinaryPrimitives.ReadUInt32BigEndian(Read(4));
			Position += 4;
			return value;
		}

		public uint PeekUInt32()
		{
			return BinaryPrimitives.ReadUInt32BigEndian(Read(4));
		}

		public ulong ReadUInt64()
		{
			ulong value = BinaryPrimitives.ReadUInt64BigEndian(Read(8));
			Position += 8;
			return value;
		}

		public void Dispose()
		{
			ArrayPool<byte>.Shared.Return(_buffer);
		}

		private ReadOnlySpan<byte> Read(int length)
		{
			if (Position < _bufferStart || Position + length > _bufferStart + _count)
			{
				_bufferStart = Position;
				_count = 0;

				while (_count < _buffer.Length)
				{
					int read = RandomAccess.Read(handle, _buffer.AsSpan(_count), _bufferStart + _count);

					if (read is 0)
					{
						break;
					}

					_count += read;
				}

				if (_count < length)
				{
					_buffer.AsSpan(_count, length - _count).Clear();
					_count = length;
				}
			}

			return _buffer.AsSpan((int)(Position - _bufferStart), length);
		}
	}
}
