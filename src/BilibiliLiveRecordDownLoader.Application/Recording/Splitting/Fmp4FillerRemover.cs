using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// 只处理数据位置算得清的分片：tfhd 以 moof 为基准且不带 base_data_offset，每个 trun 都带 data_offset，视频 trun 带每个样本的大小，各 trun 的数据都在紧跟的 mdat 里且互不重叠
// 加密和带子样本信息的分片去掉数据后附带的信息会对不上，整个原样写；sidx 记着分片的字节数，也会对不上，B 站的分片不带 sidx，遇到了就不再去
internal sealed class Fmp4FillerRemover
{
	private const int VisualSampleEntrySize = 78;

	private readonly List<Run> _runs = [];
	private readonly List<Sample> _samples = [];
	private Dictionary<uint, TrackExtends> _trackExtends = [];
	private uint? _videoTrackId;
	private VideoUnits? _videoUnits;
	private bool _hasSegmentIndex;

	// isEnabled 为 false 时也要看 moov 和 sidx
	public int Remove(Span<byte> elements, bool isEnabled)
	{
		int read = 0;
		int write = 0;

		while (read < elements.Length)
		{
			int end = read;

			if (!IsoBmff.TryReadBox(elements, ref end, out uint type, out ReadOnlySpan<byte> content))
			{
				break;
			}

			int length = end - read;

			if (type is BoxType.Moov)
			{
				ReadMovie(content);
			}
			else if (type is BoxType.Sidx)
			{
				_hasSegmentIndex = true;
			}
			else if (isEnabled && !_hasSegmentIndex && type is BoxType.Moof && TryRemove(elements, read, write, length, out int consumed, out int written))
			{
				read += consumed;
				write += written;
				continue;
			}

			write += Compaction.Keep(elements, read, write, length);
			read = end;
		}

		write += Compaction.Keep(elements, read, write, elements.Length - read);
		return write;
	}

	// 有多个样本项时分片可能换着用，长度字段宽度不一定相同
	private static VideoUnits? ReadSampleEntry(ReadOnlySpan<byte> trak)
	{
		if (!IsoBmff.TryReadSampleEntry(trak, out uint type, out ReadOnlySpan<byte> entry, out uint entryCount) || entryCount is not 1 || entry.Length < VisualSampleEntrySize)
		{
			return null;
		}

		if (type is BoxType.Av01)
		{
			return new VideoUnits.Obu();
		}

		// encv 等加密的样本项拿不到长度字段宽度
		(NalSyntax, uint)? codec = type switch
		{
			BoxType.Avc1 or BoxType.Avc3 => (NalSyntax.Avc, BoxType.AvcC),
			BoxType.Hvc1 or BoxType.Hev1 => (NalSyntax.Hevc, BoxType.HvcC),
			_ => null
		};

		if (codec is not (var syntax, var configType) || !IsoBmff.TryFindBox(entry.Slice(VisualSampleEntrySize), configType, out ReadOnlySpan<byte> config))
		{
			return null;
		}

		int lengthSize = FillerNalUnits.ReadLengthSize(config, syntax);
		return lengthSize > 0 ? new VideoUnits.Nal(syntax, lengthSize) : null;
	}

	private void ReadMovie(ReadOnlySpan<byte> moov)
	{
		_videoTrackId = null;
		_videoUnits = null;
		_trackExtends = IsoBmff.ReadTrackExtends(moov);
		int offset = 0;

		while (IsoBmff.TryReadBox(moov, ref offset, out uint type, out ReadOnlySpan<byte> trak))
		{
			if (type is BoxType.Trak && IsoBmff.IsVideoTrack(trak, out uint trackId))
			{
				_videoTrackId = trackId;
				_videoUnits = ReadSampleEntry(trak);
				break;
			}
		}
	}

	private bool TryRemove(Span<byte> elements, int read, int write, int moofLength, out int consumed, out int written)
	{
		consumed = written = 0;
		int mdatEnd = read + moofLength;

		// 大小为 0 的 mdat 一直延伸到文件末尾，改不了大小
		if (_videoTrackId is not { } videoTrackId || _videoUnits is not { } units || elements.Length - mdatEnd < 8 || BinaryPrimitives.ReadUInt32BigEndian(elements.Slice(mdatEnd)) is 0 || !IsoBmff.TryReadBox(elements, ref mdatEnd, out uint mdatType, out ReadOnlySpan<byte> payload) || mdatType is not BoxType.Mdat)
		{
			return false;
		}

		Span<byte> fragment = elements.Slice(read, mdatEnd - read);
		int payloadStart = fragment.Length - payload.Length;

		if (!TryPlan(fragment, moofLength, videoTrackId) || !TryMeasure(fragment, payloadStart, units, out int removed) || removed is 0)
		{
			return false;
		}

		// moof 只改字段的值、长度不变，前面删掉的数据让它整体前移
		Compaction.Keep(elements, read, write, payloadStart);
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
			// 没有可删的样本（包括 Measure 给出原长的全填充样本）和前后的数据一起原样搬
			if (sample.KeptLength == sample.Length)
			{
				continue;
			}

			destination += Compaction.Keep(elements, read + cursor, write + destination, sample.Offset - cursor);
			units.Move(elements, read + sample.Offset, sample.Length, write + destination);
			destination += sample.KeptLength;
			cursor = sample.Offset + sample.Length;
		}

		destination += Compaction.Keep(elements, read + cursor, write + destination, fragment.Length - cursor);
		consumed = fragment.Length;
		written = destination;
		return true;
	}

	// 记下的位置都相对 moof 开头
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

		while (IsoBmff.TryReadBox(moof, ref offset, out uint type, out ReadOnlySpan<byte> content))
		{
			if (type is BoxType.Traf)
			{
				if (!TryPlanTrack(content, moofContentStart + offset - content.Length, videoTrackId))
				{
					return false;
				}
			}
			else if (type is not BoxType.Mfhd)
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

		while (IsoBmff.TryReadBox(traf, ref offset, out uint type, out ReadOnlySpan<byte> content))
		{
			if (type is BoxType.Tfhd)
			{
				if (content.Length < 8)
				{
					return false;
				}

				uint flags = IsoBmff.ReadFlags(content);

				if ((flags & TrackFragmentFlags.DefaultBaseIsMoof) is 0 || (flags & TrackFragmentFlags.BaseDataOffsetPresent) is not 0)
				{
					return false;
				}

				trackId = BinaryPrimitives.ReadUInt32BigEndian(content.Slice(4));
				int field = TrackFragmentFlags.OffsetOf(flags, TrackFragmentFlags.DefaultSampleSizePresent);

				if ((flags & TrackFragmentFlags.DefaultSampleSizePresent) is not 0)
				{
					if (content.Length < field + 4)
					{
						return false;
					}

					defaultSampleSize = BinaryPrimitives.ReadUInt32BigEndian(content.Slice(field));
				}
			}
			else if (type is BoxType.Trun)
			{
				if (trackId is not { } id || !TryPlanRun(content, trafStart + offset - content.Length, id == videoTrackId, defaultSampleSize ?? (_trackExtends.TryGetValue(id, out TrackExtends defaults) ? defaults.DefaultSampleSize : null)))
				{
					return false;
				}
			}
			else if (type is BoxType.Senc or BoxType.Saiz or BoxType.Saio or BoxType.Subs)
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

		if ((flags & TrackRunFlags.DataOffsetPresent) is 0)
		{
			return false;
		}

		uint sampleCount = BinaryPrimitives.ReadUInt32BigEndian(trun.Slice(4));
		int dataOffset = BinaryPrimitives.ReadInt32BigEndian(trun.Slice(8));

		// 视频要逐个样本拆，必须带各自的大小；样本都是默认大小时 sample_count 不受 trun 长度约束，数据损坏时可能大得离谱，不能逐个数
		if ((flags & TrackRunFlags.SampleSizePresent) is 0)
		{
			if (isVideo || defaultSampleSize is not { } size || (ulong)sampleCount * size > int.MaxValue)
			{
				return false;
			}

			_runs.Add(new Run(trunStart + 8, dataOffset, (long)sampleCount * size));
			return true;
		}

		int stride = TrackRunFlags.SampleFieldsSize(flags);

		if (TrackRunFlags.OffsetOf(flags, TrackRunFlags.SampleDurationPresent) + sampleCount * stride > trun.Length)
		{
			return false;
		}

		long sampleOffset = dataOffset;
		int sizeField = TrackRunFlags.OffsetOf(flags, TrackRunFlags.SampleSizePresent);

		for (uint sample = 0; sample < sampleCount; ++sample, sizeField += stride)
		{
			uint size = BinaryPrimitives.ReadUInt32BigEndian(trun.Slice(sizeField));

			if (sampleOffset + size > int.MaxValue)
			{
				return false;
			}

			// 大小为 0 的样本没有可删的；它和下一个样本起点相同，放进来的话排序后可能排到那个样本后面，搬移时算出负的长度
			if (isVideo && size > 0)
			{
				_samples.Add(new Sample(trunStart + sizeField, (int)sampleOffset, (int)size, (int)size));
			}

			sampleOffset += size;
		}

		_runs.Add(new Run(trunStart + 8, dataOffset, sampleOffset - dataOffset));
		return true;
	}

	// 各 trun 的数据互不重叠，里面的样本也就不重叠，按位置排好就能一个接一个地搬
	private bool TryMeasure(ReadOnlySpan<byte> fragment, int payloadStart, VideoUnits units, out int removed)
	{
		removed = 0;
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

		foreach (ref Sample sample in CollectionsMarshal.AsSpan(_samples))
		{
			int kept = units.Measure(fragment.Slice(sample.Offset, sample.Length));

			if (kept < 0)
			{
				return false;
			}

			sample = sample with { KeptLength = kept };
			removed += sample.Length - kept;
		}

		return true;
	}

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

	// AVC 和 HEVC 的样本是带长度前缀的 NAL，AV1 的样本是自带大小的 OBU
	private closed record VideoUnits
	{
		public abstract int Measure(ReadOnlySpan<byte> sample);

		public abstract void Move(Span<byte> buffer, int source, int length, int destination);

		public sealed record Nal(NalSyntax Syntax, int LengthSize) : VideoUnits
		{
			public override int Measure(ReadOnlySpan<byte> sample)
			{
				return FillerNalUnits.Measure(sample, LengthSize, Syntax);
			}

			public override void Move(Span<byte> buffer, int source, int length, int destination)
			{
				FillerNalUnits.Move(buffer, source, length, destination, LengthSize, Syntax);
			}
		}

		public sealed record Obu : VideoUnits
		{
			public override int Measure(ReadOnlySpan<byte> sample)
			{
				return FillerObus.Measure(sample);
			}

			public override void Move(Span<byte> buffer, int source, int length, int destination)
			{
				FillerObus.Move(buffer, source, length, destination);
			}
		}
	}
}
