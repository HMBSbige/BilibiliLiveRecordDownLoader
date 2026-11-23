namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// 分片边界由会话报告；GOP 可以跨分片，要逐个检查分片开头能否独立解码
internal abstract class HlsSplitter : MediaSplitter, IHlsSegmentObserver
{
	private readonly Queue<Element> _elements = new();

	public abstract void OnInitSegment(ReadOnlySpan<byte> data);

	public abstract void OnMediaSegment(ReadOnlySpan<byte> data);

	// spans 按 Rebase 推进时间的粒度给，每个 PES 或 moof 一条；整个分片只给一条的话，时间轴认不出分片中间的重新开始
	protected void Add(int length, bool isSplit, ReadOnlyMemory<byte> header, MediaSpan[] spans)
	{
		// 空分片入队之后 Measure 会一直返回 0，后面的数据都过不去
		if (length is 0)
		{
			return;
		}

		_elements.Enqueue(new Element(length, isSplit, header, spans));
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
		if (!_elements.TryDequeue(out Element accepted))
		{
			return;
		}

		foreach (MediaSpan span in accepted.Spans)
		{
			ReportMediaTime(span);
		}
	}

	protected override ReadOnlyMemory<byte> CaptureHeader(ReadOnlySpan<byte> element)
	{
		return _elements.Peek().Header;
	}

	private readonly record struct Element(int Length, bool IsSplit, ReadOnlyMemory<byte> Header, MediaSpan[] Spans);
}

internal sealed class Fmp4HlsSplitter : HlsSplitter
{
	private readonly Fmp4FillerRemover _filler = new();
	private readonly Fmp4Rebaser _rebaser = new();
	private ReadOnlyMemory<byte> _init;
	private bool _hasMovie;
	private Fmp4VideoTrack? _video;
	private Fmp4TrackTimes _times = new([]);

	public override void OnInitSegment(ReadOnlySpan<byte> data)
	{
		_init = data.ToArray();
		_hasMovie = IsoBmff.TryReadMovie(data, out _video);
		_times = new Fmp4TrackTimes(IsoBmff.TryFindBox(data, "moov"u8, out ReadOnlySpan<byte> moov) ? IsoBmff.ReadTracks(moov) : []);
		Resolution = _video?.Resolution;
		Add(data.Length, false, default, []);
	}

	// 没有视频轨时任何分片开头都能解码
	public override void OnMediaSegment(ReadOnlySpan<byte> data)
	{
		bool isSplit = _hasMovie && (_video is not { } video || IsoBmff.StartsWithSyncSample(data, video));
		Add(data.Length, isSplit, _init, _times.ReadFragmentSpans(data));
	}

	public override void StartSegment()
	{
		_rebaser.StartSegment();
	}

	public override void Rebase(Span<byte> elements)
	{
		_rebaser.Rebase(elements);
	}

	public override int RemoveFiller(Span<byte> elements, bool isEnabled)
	{
		return _filler.Remove(elements, isEnabled);
	}
}

internal sealed class TsHlsSplitter : HlsSplitter
{
	private readonly ProgramTables _tables = new();
	private readonly TransportStreamRebaser _rebaser = new();
	private readonly List<MediaSpan> _spans = [];

	// 收下了还没改写的分片的长度：分片各自从第一个包开始改写，一个分片的包没对齐不影响同一次提交里后面的分片
	private readonly Queue<int> _unrebased = new();
	private PtsClock _clock;

	public override TimeSpan? Origin => _rebaser.Origin;

	public override void StartSegment()
	{
		_rebaser.StartSegment();
	}

	public override void Rebase(Span<byte> elements)
	{
		int offset = 0;

		while (offset < elements.Length)
		{
			Span<byte> segment = elements.Slice(offset, _unrebased.Dequeue());
			_rebaser.Rebase(segment.Slice(FindFirstPacket(segment)));
			offset += segment.Length;
		}
	}

	public override void OnInitSegment(ReadOnlySpan<byte> data)
	{
		Add(data.Length, false, default, []);
	}

	// 分片开头自带 PAT 和 PMT 时不再补，补的是这个分片之前最近的一份；开头有杂字节的分片不在它前面切
	public override void OnMediaSegment(ReadOnlySpan<byte> data)
	{
		int start = FindFirstPacket(data);
		ReadOnlySpan<byte> packets = data.Slice(start);
		ReadOnlyMemory<byte> previousTables = _tables.Header;
		bool isAligned = start is 0;
		bool isLeading = true;
		bool hasLeadingPat = false;
		bool hasLeadingPmt = false;
		_spans.Clear();

		for (int offset = 0; offset + TransportStream.PacketSize <= packets.Length; offset += TransportStream.PacketSize)
		{
			ReadOnlySpan<byte> packet = packets.Slice(offset, TransportStream.PacketSize);

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

			// 每个带时间戳的 PES 一条，所有 PID 都按解码时间，和 TransportStreamRebaser 推进的一一对应
			if (TransportStream.IsPayloadStart(packet) && TransportStream.TryReadDecodeTime(TransportStream.GetPayload(packet), out long decode))
			{
				_spans.Add(MediaSpan.At(_clock.Unwrap(decode)));
			}
		}

		bool hasOwnTables = hasLeadingPat && hasLeadingPmt;
		bool isSplit = isAligned && _tables.IsComplete && (hasOwnTables || !previousTables.IsEmpty) && StartsWithRandomAccess(packets);
		ReadResolution(packets);
		Add(data.Length, isSplit, hasOwnTables ? default : previousTables, [.. _spans]);
	}

	protected override void Accept(ReadOnlySpan<byte> element)
	{
		base.Accept(element);
		_unrebased.Enqueue(element.Length);
	}

	// 会话用 MediaProbe.IsTransportStream 认 TS 分片，开头可以有不到一个包的杂字节；杂字节里碰巧有 0x47 很常见，开头是 0x47 也要连续三个包都对得上才算
	// 找不到时返回 0
	private static int FindFirstPacket(ReadOnlySpan<byte> data)
	{
		for (int offset = 0; offset < TransportStream.PacketSize && offset + 3 * TransportStream.PacketSize <= data.Length; ++offset)
		{
			if (data[offset] is TransportStream.SyncByte && data[offset + TransportStream.PacketSize] is TransportStream.SyncByte && data[offset + 2 * TransportStream.PacketSize] is TransportStream.SyncByte)
			{
				return offset;
			}
		}

		return 0;
	}

	// 分片第一个视频 PES 开头一般带着 SPS
	private void ReadResolution(ReadOnlySpan<byte> packets)
	{
		if (_tables is not { VideoPid: >= 0 and var videoPid, VideoSyntax: { } syntax })
		{
			return;
		}

		Span<byte> pes = stackalloc byte[TransportStream.ParameterSetCapacity];
		int length = TransportStream.CopyFirstPes(packets, videoPid, pes);
		Resolution = ParameterSets.ReadAnnexB(TransportStream.SkipPesHeader(pes.Slice(0, length)), syntax) ?? Resolution;
	}

	// 节目里没有视频时任何分片开头都能解码
	private bool StartsWithRandomAccess(ReadOnlySpan<byte> packets)
	{
		return _tables.VideoPid < 0 || TransportStream.StartsWithRandomAccessPicture(packets, _tables.VideoPid, _tables.VideoSyntax);
	}
}
