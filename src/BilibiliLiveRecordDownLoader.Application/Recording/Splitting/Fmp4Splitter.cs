using System.Buffers.Binary;

namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// FFmpeg 从管道输出的分片 MP4：ftyp 和 moov 是每个文件开头要写的头，moof 连同紧跟的 mdat 是一个元素，其他顶层盒子各自原样写出
internal sealed class Fmp4Splitter : MediaSplitter
{
	// 盒子要整个收下才知道下一个从哪开始；FFmpeg 约 1 秒出一个分片，比这长得多的当作认不出的数据，不一直攒着
	private const long MaxElementLength = 64 << 20;

	private readonly Fmp4FillerRemover _filler = new();
	private readonly Fmp4Rebaser _rebaser = new();
	private byte[] _fileType = [];
	private ReadOnlyMemory<byte> _header;
	private bool _hasMovie;
	private Fmp4VideoTrack? _video;
	private Fmp4TrackTimes _times = new([]);
	private long _unparsableAt = long.MaxValue;
	private long _filtered;
	private long _removed;
	private long _rebased;

	public override bool CanSplit => !IsUnparsable;

	private bool IsUnparsable => _unparsableAt is not long.MaxValue;

	public override void StartSegment()
	{
		_rebaser.StartSegment();
	}

	// 放弃解析之后的数据不再按盒子看，里面碰巧像 moof 的字节不能改
	public override void Rebase(Span<byte> elements)
	{
		long start = _rebased;
		_rebased += elements.Length;

		// 放弃解析的位置是按收到的字节算的，去掉的填充数据都在它之前
		_rebaser.Rebase(elements.Slice(0, (int)Math.Clamp(_unparsableAt - _removed - start, 0, elements.Length)));
	}

	public override int RemoveFiller(Span<byte> elements, bool isEnabled)
	{
		long start = _filtered;
		_filtered += elements.Length;
		int end = (int)Math.Clamp(_unparsableAt - start, 0, elements.Length);
		int kept = _filler.Remove(elements.Slice(0, end), isEnabled);
		kept += FillerNalUnits.Keep(elements, end, kept, elements.Length - end);
		_removed += elements.Length - kept;
		return kept;
	}

	protected override int Measure(ReadOnlySpan<byte> data, out bool isSplit)
	{
		isSplit = false;

		if (IsUnparsable)
		{
			return data.Length;
		}

		// 流以 ftyp 开头
		long length = MeasureBox(data, AcceptedLength is 0 ? "ftyp"u8 : default);

		if (length <= 0)
		{
			return length is 0 ? 0 : GiveUp(data);
		}

		if (!data.Slice(4, 4).SequenceEqual("moof"u8))
		{
			return (int)length;
		}

		// 分片的样本数据在紧跟的 mdat 里，看到 mdat 的盒子头才知道整个分片多长
		if (data.Length < length)
		{
			return 0;
		}

		ReadOnlySpan<byte> moof = data.Slice(0, (int)length);
		long mdatLength = MeasureBox(data.Slice(moof.Length), "mdat"u8);

		// 后面不是 mdat 的 moof 单独算一个元素，不在它前面切
		if (mdatLength <= 0)
		{
			return mdatLength is 0 ? 0 : moof.Length;
		}

		if (length + mdatLength > MaxElementLength)
		{
			return GiveUp(data);
		}

		isSplit = _hasMovie && (_video is not { } video || IsoBmff.StartsWithSyncSample(moof, video));
		return (int)(length + mdatLength);
	}

	protected override void Accept(ReadOnlySpan<byte> element)
	{
		// 开头就认不出的数据可能不到一个盒子头
		if (IsUnparsable)
		{
			return;
		}

		ReadOnlySpan<byte> type = element.Slice(4, 4);

		if (type.SequenceEqual("ftyp"u8))
		{
			_fileType = element.ToArray();
		}
		else if (type.SequenceEqual("moov"u8))
		{
			_hasMovie = IsoBmff.TryReadMovie(element, out _video);
			_times = new Fmp4TrackTimes(IsoBmff.TryFindBox(element, "moov"u8, out ReadOnlySpan<byte> moov) ? IsoBmff.ReadTracks(moov) : []);
			Resolution = _video?.Resolution;
			_header = (byte[])[.. _fileType, .. element];
		}
		else if (type.SequenceEqual("moof"u8))
		{
			foreach (MediaSpan span in _times.ReadFragmentSpans(element))
			{
				ReportMediaTime(span);
			}
		}
	}

	protected override ReadOnlyMemory<byte> CaptureHeader()
	{
		return _header;
	}

	// 返回 data 开头的盒子的总长，还判断不了时返回 0，认不出时返回 -1；type 不为空时盒子必须是这个类型
	// 收到多少看多少，认不出时不等凑够盒子头：顶层盒子的类型都是可打印的 ASCII，大小为 0 的盒子延伸到文件末尾，在流里找不到尽头
	private static long MeasureBox(ReadOnlySpan<byte> data, ReadOnlySpan<byte> type)
	{
		ReadOnlySpan<byte> receivedType = data.Slice(Math.Min(data.Length, 4), Math.Clamp(data.Length - 4, 0, 4));

		if (data.Length >= 4 && BinaryPrimitives.ReadUInt32BigEndian(data) is 0 or (> 1 and < 8) || receivedType.ContainsAnyExceptInRange((byte)' ', (byte)'~') || !type.IsEmpty && !type.StartsWith(receivedType))
		{
			return -1;
		}

		if (data.Length < 8)
		{
			return 0;
		}

		ulong size = BinaryPrimitives.ReadUInt32BigEndian(data);

		if (size is 1)
		{
			if (data.Length < 16)
			{
				return 0;
			}

			size = BinaryPrimitives.ReadUInt64BigEndian(data.Slice(8));

			if (size < 16)
			{
				return -1;
			}
		}

		return size <= MaxElementLength ? (long)size : -1;
	}

	private int GiveUp(ReadOnlySpan<byte> data)
	{
		_unparsableAt = AcceptedLength;
		return data.Length;
	}
}
