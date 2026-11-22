using System.Buffers.Binary;

namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// 按顺序看提交的顶层盒子：moov 给出视频轨和 NAL 长度字段的宽度，moof 和紧跟的 mdat 删掉视频样本里的填充 NAL，再改 trun 的样本大小、data_offset 和 mdat 的大小。
// 只处理数据位置能完全算清楚的分片：tfhd 以 moof 为基准且不带 base_data_offset，每个 trun 都带 data_offset，视频 trun 带每个样本的大小，各 trun 的数据都在紧跟的 mdat 里且互不重叠。
// 加密和带子样本信息的分片去掉数据后附带的信息会对不上，和其他算不清的分片一样整个原样写。
// sidx 记着它引用的分片的字节数，分片变短之后也会对不上；B 站的分片不带 sidx，遇到了就不再去
internal sealed class Fmp4FillerRemover
{
	private const int VisualSampleEntrySize = 78;
	private const uint BaseDataOffsetPresent = 0x00_0001;
	private const uint SampleDescriptionIndexPresent = 0x00_0002;
	private const uint DefaultSampleDurationPresent = 0x00_0008;
	private const uint DefaultSampleSizePresent = 0x00_0010;
	private const uint DefaultBaseIsMoof = 0x02_0000;
	private const uint DataOffsetPresent = 0x00_0001;
	private const uint FirstSampleFlagsPresent = 0x00_0004;
	private const uint SampleDurationPresent = 0x00_0100;
	private const uint SampleSizePresent = 0x00_0200;
	private const uint SampleFlagsPresent = 0x00_0400;
	private const uint SampleCompositionTimeOffsetPresent = 0x00_0800;

	private readonly Dictionary<uint, uint> _defaultSampleSizes = [];
	private readonly List<Run> _runs = [];
	private readonly List<Sample> _samples = [];
	private uint? _videoTrackId;
	private NalSyntax _syntax;
	private int _nalLengthSize;
	private bool _hasSegmentIndex;
	private bool _isStopped;

	// 返回去掉之后的长度。关闭时也要看 moov 和 sidx；不完整的盒子只会在收流结束时出现在末尾，之后不再解析
	public int Remove(Span<byte> elements, bool isEnabled)
	{
		int read = 0;
		int write = 0;

		while (!_isStopped && read < elements.Length)
		{
			int end = read;

			if (!IsoBmff.TryReadBox(elements, ref end, out ReadOnlySpan<byte> type, out ReadOnlySpan<byte> content))
			{
				_isStopped = true;
				break;
			}

			int length = end - read;

			if (type.SequenceEqual("moov"u8))
			{
				ReadMovie(content);
			}
			else if (type.SequenceEqual("sidx"u8))
			{
				_hasSegmentIndex = true;
			}
			else if (isEnabled && !_hasSegmentIndex && type.SequenceEqual("moof"u8) && TryRemove(elements, read, write, length, out int consumed, out int written))
			{
				read += consumed;
				write += written;
				continue;
			}

			write += Keep(elements, read, write, length);
			read = end;
		}

		write += Keep(elements, read, write, elements.Length - read);
		return write;
	}

	private static int Keep(Span<byte> elements, int read, int write, int length)
	{
		if (read != write)
		{
			elements.Slice(read, length).CopyTo(elements.Slice(write));
		}

		return length;
	}

	// 解码器配置在视觉样本项的固定字段之后；有多个样本项时分片可能换着用，宽度不一定相同
	private static (NalSyntax Syntax, int LengthSize) ReadSampleEntry(ReadOnlySpan<byte> trak)
	{
		if (!IsoBmff.TryFindBox(trak, "mdia"u8, out ReadOnlySpan<byte> mdia) || !IsoBmff.TryFindBox(mdia, "minf"u8, out ReadOnlySpan<byte> minf) || !IsoBmff.TryFindBox(minf, "stbl"u8, out ReadOnlySpan<byte> stbl) || !IsoBmff.TryFindBox(stbl, "stsd"u8, out ReadOnlySpan<byte> stsd))
		{
			return default;
		}

		int offset = 8;

		if (stsd.Length < offset || BinaryPrimitives.ReadUInt32BigEndian(stsd.Slice(4)) is not 1 || !IsoBmff.TryReadBox(stsd, ref offset, out ReadOnlySpan<byte> type, out ReadOnlySpan<byte> entry) || entry.Length < VisualSampleEntrySize)
		{
			return default;
		}

		NalSyntax syntax;
		ReadOnlySpan<byte> configType;

		if (type.SequenceEqual("avc1"u8) || type.SequenceEqual("avc3"u8))
		{
			syntax = NalSyntax.Avc;
			configType = "avcC"u8;
		}
		else if (type.SequenceEqual("hvc1"u8) || type.SequenceEqual("hev1"u8))
		{
			syntax = NalSyntax.Hevc;
			configType = "hvcC"u8;
		}
		else
		{
			// encv 等加密的样本项拿不到长度字段宽度
			return default;
		}

		if (!IsoBmff.TryFindBox(entry.Slice(VisualSampleEntrySize), configType, out ReadOnlySpan<byte> config))
		{
			return default;
		}

		return (syntax, FillerNalUnits.ReadLengthSize(config, syntax));
	}

	private void ReadMovie(ReadOnlySpan<byte> moov)
	{
		_videoTrackId = null;
		_nalLengthSize = 0;
		_defaultSampleSizes.Clear();
		int offset = 0;

		while (IsoBmff.TryReadBox(moov, ref offset, out ReadOnlySpan<byte> type, out ReadOnlySpan<byte> content))
		{
			if (type.SequenceEqual("trak"u8) && _videoTrackId is null && IsoBmff.IsVideoTrack(content, out uint trackId))
			{
				_videoTrackId = trackId;
				(_syntax, _nalLengthSize) = ReadSampleEntry(content);
			}
			else if (type.SequenceEqual("mvex"u8))
			{
				int trexOffset = 0;

				// version/flags、track_ID、default_sample_description_index、default_sample_duration、default_sample_size
				while (IsoBmff.TryReadBox(content, ref trexOffset, out ReadOnlySpan<byte> trexType, out ReadOnlySpan<byte> trex))
				{
					if (trexType.SequenceEqual("trex"u8) && trex.Length >= 20)
					{
						_defaultSampleSizes[BinaryPrimitives.ReadUInt32BigEndian(trex.Slice(4))] = BinaryPrimitives.ReadUInt32BigEndian(trex.Slice(16));
					}
				}
			}
		}
	}

	// moof 从 read 开始；没有可删的或者结构算不清时返回 false，交给调用方原样写
	private bool TryRemove(Span<byte> elements, int read, int write, int moofLength, out int consumed, out int written)
	{
		consumed = written = 0;
		int mdatEnd = read + moofLength;

		// 大小为 0 的 mdat 一直延伸到文件末尾，改不了大小
		if (_videoTrackId is not { } videoTrackId || _nalLengthSize is 0 || elements.Length - mdatEnd < 8 || BinaryPrimitives.ReadUInt32BigEndian(elements.Slice(mdatEnd)) is 0 || !IsoBmff.TryReadBox(elements, ref mdatEnd, out ReadOnlySpan<byte> mdatType, out ReadOnlySpan<byte> payload) || !mdatType.SequenceEqual("mdat"u8))
		{
			return false;
		}

		Span<byte> fragment = elements.Slice(read, mdatEnd - read);
		int payloadStart = fragment.Length - payload.Length;

		if (!TryPlan(fragment, moofLength, videoTrackId) || !TryMeasure(fragment, payloadStart))
		{
			return false;
		}

		int removed = 0;

		foreach (Sample sample in _samples)
		{
			removed += sample.Length - sample.KeptLength;
		}

		if (removed is 0)
		{
			return false;
		}

		// moof 只改字段的值，长度不变；前面已经删掉的数据让它整体前移
		Keep(elements, read, write, payloadStart);
		Span<byte> output = elements.Slice(write);

		foreach (Sample sample in _samples)
		{
			BinaryPrimitives.WriteUInt32BigEndian(output.Slice(sample.SizeField), (uint)sample.KeptLength);
		}

		foreach (Run run in _runs)
		{
			BinaryPrimitives.WriteInt32BigEndian(output.Slice(run.OffsetField), run.DataOffset - RemovedBefore(run.DataOffset));
		}

		int mdatHeaderSize = payloadStart - moofLength;
		long mdatSize = fragment.Length - moofLength - removed;

		if (mdatHeaderSize is 16)
		{
			BinaryPrimitives.WriteUInt64BigEndian(output.Slice(moofLength + 8), (ulong)mdatSize);
		}
		else
		{
			BinaryPrimitives.WriteUInt32BigEndian(output.Slice(moofLength), (uint)mdatSize);
		}

		int cursor = payloadStart;
		int destination = payloadStart;

		foreach (Sample sample in _samples)
		{
			destination += Keep(elements, read + cursor, write + destination, sample.Offset - cursor);
			FillerNalUnits.Move(elements, read + sample.Offset, sample.Length, write + destination, sample.KeptLength, _nalLengthSize, _syntax);
			destination += sample.KeptLength;
			cursor = sample.Offset + sample.Length;
		}

		destination += Keep(elements, read + cursor, write + destination, fragment.Length - cursor);
		consumed = fragment.Length;
		written = destination;
		return true;
	}

	// 记下每个 trun 的数据范围和 data_offset 字段的位置，以及视频样本的范围和大小字段的位置，位置都相对 moof 开头
	private bool TryPlan(ReadOnlySpan<byte> fragment, int moofLength, uint videoTrackId)
	{
		_runs.Clear();
		_samples.Clear();
		int moofOffset = 0;

		if (!IsoBmff.TryReadBox(fragment, ref moofOffset, out _, out ReadOnlySpan<byte> moof))
		{
			return false;
		}

		int moofContentStart = moofLength - moof.Length;
		int offset = 0;

		while (IsoBmff.TryReadBox(moof, ref offset, out ReadOnlySpan<byte> type, out ReadOnlySpan<byte> content))
		{
			if (type.SequenceEqual("traf"u8))
			{
				if (!TryPlanTrack(content, moofContentStart + offset - content.Length, videoTrackId))
				{
					return false;
				}
			}
			else if (!type.SequenceEqual("mfhd"u8))
			{
				// pssh 之类的盒子说明分片加密了
				return false;
			}
		}

		return offset == moof.Length;
	}

	private bool TryPlanTrack(ReadOnlySpan<byte> traf, int trafStart, uint videoTrackId)
	{
		uint? trackId = null;
		uint? defaultSampleSize = null;
		int offset = 0;

		while (IsoBmff.TryReadBox(traf, ref offset, out ReadOnlySpan<byte> type, out ReadOnlySpan<byte> content))
		{
			if (type.SequenceEqual("tfhd"u8))
			{
				if (content.Length < 8)
				{
					return false;
				}

				uint flags = IsoBmff.ReadFlags(content);

				if ((flags & DefaultBaseIsMoof) is 0 || (flags & BaseDataOffsetPresent) is not 0)
				{
					return false;
				}

				trackId = BinaryPrimitives.ReadUInt32BigEndian(content.Slice(4));
				int field = 8 + ((flags & SampleDescriptionIndexPresent) is 0 ? 0 : 4) + ((flags & DefaultSampleDurationPresent) is 0 ? 0 : 4);

				if ((flags & DefaultSampleSizePresent) is not 0)
				{
					if (content.Length < field + 4)
					{
						return false;
					}

					defaultSampleSize = BinaryPrimitives.ReadUInt32BigEndian(content.Slice(field));
				}
			}
			else if (type.SequenceEqual("trun"u8))
			{
				if (trackId is not { } id || !TryPlanRun(content, trafStart + offset - content.Length, id == videoTrackId, defaultSampleSize ?? (_defaultSampleSizes.TryGetValue(id, out uint size) ? size : null)))
				{
					return false;
				}
			}
			else if (type.SequenceEqual("senc"u8) || type.SequenceEqual("saiz"u8) || type.SequenceEqual("saio"u8) || type.SequenceEqual("subs"u8))
			{
				return false;
			}
		}

		return offset == traf.Length;
	}

	private bool TryPlanRun(ReadOnlySpan<byte> trun, int trunStart, bool isVideo, uint? defaultSampleSize)
	{
		if (trun.Length < 12)
		{
			return false;
		}

		uint flags = IsoBmff.ReadFlags(trun);
		bool hasSampleSizes = (flags & SampleSizePresent) is not 0;

		if ((flags & DataOffsetPresent) is 0 || (isVideo && !hasSampleSizes) || (!hasSampleSizes && defaultSampleSize is null))
		{
			return false;
		}

		uint sampleCount = BinaryPrimitives.ReadUInt32BigEndian(trun.Slice(4));
		int dataOffset = BinaryPrimitives.ReadInt32BigEndian(trun.Slice(8));

		// 样本都是默认大小时 sample_count 不受 trun 长度约束，数据损坏时可能大得离谱，不能逐个数
		if (!hasSampleSizes)
		{
			ulong runLength = (ulong)sampleCount * defaultSampleSize.GetValueOrDefault();

			if (runLength > int.MaxValue)
			{
				return false;
			}

			_runs.Add(new Run(trunStart + 8, dataOffset, (long)runLength));
			return true;
		}

		int field = 12 + ((flags & FirstSampleFlagsPresent) is 0 ? 0 : 4);
		int sizeField = (flags & SampleDurationPresent) is 0 ? 0 : 4;
		int sampleFieldsSize = sizeField + 4 + ((flags & SampleFlagsPresent) is 0 ? 0 : 4) + ((flags & SampleCompositionTimeOffsetPresent) is 0 ? 0 : 4);

		if (field + sampleCount * sampleFieldsSize > trun.Length)
		{
			return false;
		}

		long sampleOffset = dataOffset;

		for (uint sample = 0; sample < sampleCount; ++sample, field += sampleFieldsSize)
		{
			uint size = BinaryPrimitives.ReadUInt32BigEndian(trun.Slice(field + sizeField));

			if (sampleOffset + size > int.MaxValue)
			{
				return false;
			}

			// 大小为 0 的样本和下一个样本起点相同，没有可删的；放进来的话排序时分不清它和下一个样本的先后
			if (isVideo && size > 0)
			{
				_samples.Add(new Sample(trunStart + field + sizeField, (int)sampleOffset, (int)size, (int)size));
			}

			sampleOffset += size;
		}

		_runs.Add(new Run(trunStart + 8, dataOffset, sampleOffset - dataOffset));
		return true;
	}

	// 各 trun 的数据都要落在 mdat 的数据里且互不重叠，再逐个量出视频样本去掉填充之后的长度
	private bool TryMeasure(Span<byte> fragment, int payloadStart)
	{
		_runs.Sort(static (x, y) => x.DataOffset.CompareTo(y.DataOffset));
		long previousEnd = payloadStart;

		foreach (Run run in _runs)
		{
			if (run.DataOffset < previousEnd || run.DataOffset + run.Length > fragment.Length)
			{
				return false;
			}

			previousEnd = run.DataOffset + run.Length;
		}

		_samples.Sort(static (x, y) => x.Offset.CompareTo(y.Offset));
		long previousSampleEnd = payloadStart;

		for (int index = 0; index < _samples.Count; ++index)
		{
			Sample sample = _samples[index];

			// 搬移时按顺序一个接一个地拷，样本有重叠就会算出负的长度
			if (sample.Offset < previousSampleEnd)
			{
				return false;
			}

			previousSampleEnd = sample.Offset + sample.Length;
			int kept = FillerNalUnits.Measure(fragment.Slice(sample.Offset, sample.Length), _nalLengthSize, _syntax);

			if (kept < 0)
			{
				return false;
			}

			_samples[index] = sample with { KeptLength = kept };
		}

		return true;
	}

	// 范围不重叠，数据在 offset 之前的样本整个都在它之前
	private int RemovedBefore(int offset)
	{
		int removed = 0;

		foreach (Sample sample in _samples)
		{
			if (sample.Offset >= offset)
			{
				break;
			}

			removed += sample.Length - sample.KeptLength;
		}

		return removed;
	}

	private readonly record struct Run(int OffsetField, int DataOffset, long Length);

	private readonly record struct Sample(int SizeField, int Offset, int Length, int KeptLength);
}
