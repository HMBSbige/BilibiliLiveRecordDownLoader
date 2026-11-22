namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// HLS 由会话报告分片边界，只在开头就能独立解码的分片前切；分片是否以关键帧开头要逐个检查，GOP 可以跨分片
internal abstract class HlsSplitter : MediaSplitter, IHlsSegmentObserver
{
	private readonly Queue<Element> _elements = new();

	public abstract void OnInitSegment(ReadOnlySpan<byte> data);

	public abstract void OnMediaSegment(ReadOnlySpan<byte> data);

	protected void Add(int length, bool isSplit, ReadOnlyMemory<byte> header)
	{
		_elements.Enqueue(new Element(length, isSplit, header));
	}

	protected override int Measure(ReadOnlySpan<byte> data, out bool isSplit)
	{
		if (!_elements.TryPeek(out Element element))
		{
			// 会话总是先报告再写出，没有报告的数据只能原样跟在后面
			isSplit = false;
			return data.Length;
		}

		isSplit = element.IsSplit;
		return element.Length;
	}

	protected override void Accept(ReadOnlySpan<byte> element)
	{
		_elements.TryDequeue(out _);
	}

	protected override ReadOnlyMemory<byte> CaptureHeader(ReadOnlySpan<byte> element)
	{
		return _elements.Peek().Header;
	}

	private readonly record struct Element(int Length, bool IsSplit, ReadOnlyMemory<byte> Header);
}

internal sealed class Fmp4HlsSplitter : HlsSplitter
{
	private ReadOnlyMemory<byte> _init;
	private bool _hasTracks;
	private uint? _videoTrackId;
	private uint _defaultSampleFlags;

	public override void OnInitSegment(ReadOnlySpan<byte> data)
	{
		_init = data.ToArray();
		_hasTracks = IsoBmff.TryReadVideoTrack(data, out _videoTrackId, out _defaultSampleFlags);
		Add(data.Length, false, default);
	}

	// 每段都从 init 段开始；没有视频轨时任何分片开头都能解码
	public override void OnMediaSegment(ReadOnlySpan<byte> data)
	{
		bool isSplit = _hasTracks && (_videoTrackId is not { } trackId || IsoBmff.StartsWithSyncSample(data, trackId, _defaultSampleFlags));
		Add(data.Length, isSplit, _init);
	}
}

internal sealed class TsHlsSplitter : HlsSplitter
{
	private readonly ProgramTables _tables = new();

	public override void OnInitSegment(ReadOnlySpan<byte> data)
	{
		Add(data.Length, false, default);
	}

	// 分片开头自带 PAT 和 PMT 时不再补；补的是这个分片之前最近的一份
	public override void OnMediaSegment(ReadOnlySpan<byte> data)
	{
		ReadOnlyMemory<byte> previousTables = _tables.Header;
		bool isLeading = true;
		bool hasLeadingPat = false;
		bool hasLeadingPmt = false;
		bool isAligned = true;

		for (int offset = 0; offset + TransportStream.PacketSize <= data.Length; offset += TransportStream.PacketSize)
		{
			ReadOnlySpan<byte> packet = data.Slice(offset, TransportStream.PacketSize);

			if (packet[0] is not TransportStream.SyncByte)
			{
				isAligned = false;
				break;
			}

			if (_tables.Inspect(packet))
			{
				bool isPat = TransportStream.GetPid(packet) is 0;
				hasLeadingPat |= isLeading && isPat;
				hasLeadingPmt |= isLeading && !isPat;
			}
			else
			{
				isLeading = false;
			}
		}

		bool hasOwnTables = hasLeadingPat && hasLeadingPmt;
		bool isSplit = isAligned && _tables.IsComplete && (hasOwnTables || !previousTables.IsEmpty) && StartsWithRandomAccess(data);
		Add(data.Length, isSplit, hasOwnTables ? default : previousTables);
	}

	private bool StartsWithRandomAccess(ReadOnlySpan<byte> data)
	{
		return _tables.VideoPid is var videoPid and >= 0 ? TransportStream.StartsWithRandomAccessPicture(data, videoPid, _tables.VideoCodec) : _tables.VideoCodec is TransportVideoCodec.None;
	}
}
