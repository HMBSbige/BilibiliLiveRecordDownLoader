namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// HLS 由会话报告分片边界，只在开头就能独立解码的分片前切；分片是否以关键帧开头要逐个检查，GOP 可以跨分片
internal abstract class HlsSplitter : MediaSplitter, IHlsSegmentObserver
{
	private readonly Queue<Element> _elements = new();

	public abstract void OnInitSegment(ReadOnlySpan<byte> data);

	public abstract void OnMediaSegment(ReadOnlySpan<byte> data);

	// span 是分片从第一帧到最后一帧的媒体时间，会话报告时就算好，分片过去时再报告
	protected void Add(int length, bool isSplit, ReadOnlyMemory<byte> header, MediaSpan? span)
	{
		_elements.Enqueue(new Element(length, isSplit, header, span));
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
		if (_elements.TryDequeue(out Element accepted) && accepted.Span is { } span)
		{
			ReportMediaTime(span);
		}
	}

	protected override ReadOnlyMemory<byte> CaptureHeader(ReadOnlySpan<byte> element)
	{
		return _elements.Peek().Header;
	}

	private readonly record struct Element(int Length, bool IsSplit, ReadOnlyMemory<byte> Header, MediaSpan? Span);
}

internal sealed class Fmp4HlsSplitter : HlsSplitter
{
	private readonly Fmp4FillerRemover _filler = new();
	private ReadOnlyMemory<byte> _init;
	private bool _hasTracks;
	private uint? _videoTrackId;
	private uint _defaultSampleFlags;
	private Fmp4VideoTrack? _timing;

	public override void OnInitSegment(ReadOnlySpan<byte> data)
	{
		_init = data.ToArray();
		_hasTracks = IsoBmff.TryReadVideoTrack(data, out _videoTrackId, out _defaultSampleFlags);
		_timing = IsoBmff.ReadVideoTrackTiming(data);
		Resolution = _timing?.Resolution;
		Add(data.Length, false, default, null);
	}

	// 每段都从 init 段开始；没有视频轨时任何分片开头都能解码。分段不改 tfdt，文件里的时间就是源时间；用解码时间，不看合成时间偏移，只差几帧
	public override void OnMediaSegment(ReadOnlySpan<byte> data)
	{
		bool isSplit = _hasTracks && (_videoTrackId is not { } trackId || IsoBmff.StartsWithSyncSample(data, trackId, _defaultSampleFlags));
		MediaSpan? span = _timing is { } timing && IsoBmff.ReadFragmentTime(data, timing) is (var start, var end) ? new MediaSpan(timing.ToTime(start), timing.ToTime(end)) : null;
		Add(data.Length, isSplit, _init, span);
	}

	public override int RemoveFiller(Span<byte> elements, bool isEnabled)
	{
		return _filler.Remove(elements, isEnabled);
	}
}

internal sealed class TsHlsSplitter : HlsSplitter
{
	private readonly ProgramTables _tables = new();
	private PtsClock _clock;

	public override void OnInitSegment(ReadOnlySpan<byte> data)
	{
		Add(data.Length, false, default, null);
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
		ReadResolution(data);
		Add(data.Length, isSplit, hasOwnTables ? default : previousTables, ReadSpan(data));
	}

	// 视频 PES 的 PTS 按分片顺序展开回绕，取最早和最晚的；B 帧让 PTS 不按顺序出现
	private MediaSpan? ReadSpan(ReadOnlySpan<byte> data)
	{
		TimeSpan? start = null;
		TimeSpan end = TimeSpan.MinValue;

		for (int offset = 0; _tables.VideoPid >= 0 && offset + TransportStream.PacketSize <= data.Length; offset += TransportStream.PacketSize)
		{
			ReadOnlySpan<byte> packet = data.Slice(offset, TransportStream.PacketSize);

			if (packet[0] is not TransportStream.SyncByte)
			{
				break;
			}

			if (TransportStream.GetPid(packet) == _tables.VideoPid && TransportStream.IsPayloadStart(packet) && TransportStream.TryReadPts(TransportStream.GetPayload(packet), out long pts))
			{
				TimeSpan time = _clock.Unwrap(pts);
				start = start is { } earliest && earliest < time ? earliest : time;
				end = time > end ? time : end;
			}
		}

		return start is { } value ? new MediaSpan(value, end) : null;
	}

	// 分片第一个视频 PES 前面一般带着 SPS；不带的分片沿用之前读到的尺寸
	private void ReadResolution(ReadOnlySpan<byte> data)
	{
		if (_tables is not { VideoPid: >= 0, VideoCodec: TransportVideoCodec.Avc or TransportVideoCodec.Hevc })
		{
			return;
		}

		Span<byte> pes = stackalloc byte[TransportStream.ParameterSetCapacity];
		int length = TransportStream.CopyFirstPes(data, _tables.VideoPid, pes);
		Resolution = ParameterSets.ReadAnnexB(TransportStream.SkipPesHeader(pes.Slice(0, length)), _tables.VideoCodec is TransportVideoCodec.Hevc) ?? Resolution;
	}

	private bool StartsWithRandomAccess(ReadOnlySpan<byte> data)
	{
		return _tables.VideoPid is var videoPid and >= 0 ? TransportStream.StartsWithRandomAccessPicture(data, videoPid, _tables.VideoCodec) : _tables.VideoCodec is TransportVideoCodec.None;
	}
}
