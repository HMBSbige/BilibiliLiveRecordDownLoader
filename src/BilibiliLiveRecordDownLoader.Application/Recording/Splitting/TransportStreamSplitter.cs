namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

internal sealed class TransportStreamSplitter : MediaSplitter
{
	private readonly ProgramTables _tables = new();
	private readonly byte[] _parameterSets = new byte[TransportStream.ParameterSetCapacity];
	private readonly TransportStreamRebaser _rebaser = new();
	private long _unalignedAt = long.MaxValue;
	private long _rebased;
	private PtsClock _clock;

	private bool _hasUnrecognizedVideo;

	// 正在收集的 PES 开头已经存下的字节数，不在收集时为 -1
	private int _parameterSetLength = -1;

	public override bool CanSplit => !IsUnaligned;

	// 从管道读到的第一次写入常常只有关键帧 PES 的开头，时间更早的音频 PES 在后面的写入里
	public override bool HoldsFirstUnit => true;

	private bool IsUnaligned => _unalignedAt is not long.MaxValue;

	public override TimeSpan? Origin => _rebaser.Origin;

	public override void StartSegment()
	{
		_rebaser.StartSegment();
	}

	// 失去包同步之后的数据不再按包看，里面碰巧像包的字节不能改
	public override void Rebase(Span<byte> elements)
	{
		long start = _rebased;
		_rebased += elements.Length;
		_rebaser.Rebase(elements.Slice(0, (int)Math.Clamp(_unalignedAt - start, 0, elements.Length)));
	}

	protected override int Measure(ReadOnlySpan<byte> data, out bool isSplit)
	{
		isSplit = false;

		if (IsUnaligned)
		{
			return data.Length;
		}

		// 失去包同步之后找不到包的边界，只能不再切；不等凑够一个包
		if (data[0] is not TransportStream.SyncByte)
		{
			_unalignedAt = AcceptedLength;
			return data.Length;
		}

		if (data.Length < TransportStream.PacketSize)
		{
			return 0;
		}

		ReadOnlySpan<byte> packet = data.Slice(0, TransportStream.PacketSize);
		isSplit = _tables.IsComplete && TransportStream.IsPayloadStart(packet) && StartsRandomAccess(packet);
		return TransportStream.PacketSize;
	}

	protected override void Accept(ReadOnlySpan<byte> element)
	{
		if (IsUnaligned || _tables.Inspect(element))
		{
			return;
		}

		ReadOnlySpan<byte> payload = TransportStream.GetPayload(element);
		bool isVideo = TransportStream.GetPid(element) == _tables.VideoPid;

		if (!TransportStream.IsPayloadStart(element))
		{
			if (isVideo)
			{
				CollectParameterSets(payload);
			}

			return;
		}

		if (isVideo)
		{
			// SPS 跟在关键帧前面；读到尺寸之后仍看每个关键帧，尺寸中途变了也能跟上
			_parameterSetLength = _tables.VideoSyntax is not null && (Resolution is null || TransportStream.IsRandomAccess(element)) ? 0 : -1;
			CollectParameterSets(TransportStream.SkipPesHeader(payload));
		}

		// 先读完同一个包里的 SPS 再报告时间，从这个关键帧开始的新段才带新尺寸
		if (TransportStream.TryReadDecodeTime(payload, out long time))
		{
			ReportMediaTime(MediaSpan.At(_clock.Unwrap(time)));
		}
	}

	protected override ReadOnlyMemory<byte> CaptureHeader()
	{
		return _tables.Header;
	}

	// 视频在带随机访问标志的 PES 前切，FFmpeg 只给关键帧设这个标志；节目里没有视频时和 HLS 一样，任何带时间的 PES 开头都能解码
	// 有认不出的视频时在哪个 PES 前切，新段都可能从 GOP 中间开始，只能不切
	private bool StartsRandomAccess(ReadOnlySpan<byte> packet)
	{
		if (_tables.VideoPid >= 0)
		{
			return TransportStream.GetPid(packet) == _tables.VideoPid && TransportStream.IsRandomAccess(packet);
		}

		ReadOnlySpan<byte> payload = TransportStream.GetPayload(packet);
		_hasUnrecognizedVideo |= payload is [0, 0, 1, >= 0xE0 and <= 0xEF, ..];
		return !_hasUnrecognizedVideo && TransportStream.TryReadDecodeTime(payload, out _);
	}

	private void CollectParameterSets(ReadOnlySpan<byte> payload)
	{
		if (_parameterSetLength < 0 || _tables.VideoSyntax is not { } syntax)
		{
			return;
		}

		_parameterSetLength += TransportStream.Append(payload, _parameterSets.AsSpan(_parameterSetLength));

		if (ParameterSets.ReadAnnexB(_parameterSets.AsSpan(0, _parameterSetLength), syntax) is { } resolution)
		{
			Resolution = resolution;
			_parameterSetLength = -1;
		}
		else if (_parameterSetLength == _parameterSets.Length)
		{
			_parameterSetLength = -1;
		}
	}
}
