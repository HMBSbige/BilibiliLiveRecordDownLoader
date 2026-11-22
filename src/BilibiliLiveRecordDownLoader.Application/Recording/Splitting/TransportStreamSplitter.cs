namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// FFmpeg 录制输出的 MPEG-TS 在带随机访问标志的视频 PES 开头之前切，FFmpeg 只给关键帧设置这个标志；新段开头补最近的 PAT 和 PMT
internal sealed class TransportStreamSplitter : MediaSplitter
{
	private readonly ProgramTables _tables = new();
	private bool _isUnaligned;

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
		if (!_isUnaligned)
		{
			_tables.Inspect(element);
		}
	}

	protected override ReadOnlyMemory<byte> CaptureHeader(ReadOnlySpan<byte> element)
	{
		return _tables.Header;
	}
}
