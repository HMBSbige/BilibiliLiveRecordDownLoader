namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// FFmpeg 录制输出的 MPEG-TS 在带随机访问标志的视频 PES 开头之前切，FFmpeg 只给关键帧设置这个标志；新段开头补最近的 PAT 和 PMT
internal sealed class TransportStreamSplitter : MediaSplitter
{
	private readonly ProgramTables _tables = new();
	private readonly byte[] _parameterSets = new byte[TransportStream.ParameterSetCapacity];
	private bool _isUnaligned;
	private PtsClock _clock;

	// 正在收集的 PES 开头已经存下的字节数，不在收集时为 -1
	private int _parameterSetLength = -1;

	public override bool CanSplit => !_isUnaligned;

	protected override int Measure(ReadOnlySpan<byte> data, out bool isSplit)
	{
		isSplit = false;

		if (_isUnaligned)
		{
			return data.Length;
		}

		if (data.Length < TransportStream.PacketSize)
		{
			return 0;
		}

		// 失去包同步之后再也找不到包的边界，只能不再切
		if (data[0] is not TransportStream.SyncByte)
		{
			_isUnaligned = true;
			return data.Length;
		}

		isSplit = _tables.IsComplete && TransportStream.GetPid(data) == _tables.VideoPid && TransportStream.IsPayloadStart(data) && TransportStream.IsRandomAccess(data);
		return TransportStream.PacketSize;
	}

	protected override void Accept(ReadOnlySpan<byte> element)
	{
		if (_isUnaligned || _tables.Inspect(element) || TransportStream.GetPid(element) != _tables.VideoPid)
		{
			return;
		}

		ReadOnlySpan<byte> payload = TransportStream.GetPayload(element);

		if (!TransportStream.IsPayloadStart(element))
		{
			CollectParameterSets(payload);
			return;
		}

		bool hasPts = TransportStream.TryReadPts(payload, out long pts);

		// SPS 跟在关键帧前面；还没读到尺寸时每个 PES 都看，读到之后只看关键帧，尺寸中途变了也能跟上
		_parameterSetLength = _tables.VideoSyntax is not null && (Resolution is null || TransportStream.IsRandomAccess(element)) ? 0 : -1;
		CollectParameterSets(TransportStream.SkipPesHeader(payload));

		// 时间带着当时的尺寸，先读完同一个包里的 SPS，从这个关键帧开始的新段才是新尺寸
		if (hasPts)
		{
			ReportMediaTime(MediaSpan.At(_clock.Unwrap(pts)));
		}
	}

	protected override ReadOnlyMemory<byte> CaptureHeader(ReadOnlySpan<byte> element)
	{
		return _tables.Header;
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
