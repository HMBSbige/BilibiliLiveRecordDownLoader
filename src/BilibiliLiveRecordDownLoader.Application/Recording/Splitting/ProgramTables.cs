namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// 新段开头要补 PAT 和 PMT，否则播放器不知道各个 PID 是什么
internal sealed class ProgramTables
{
	private byte[]? _pat;
	private byte[]? _pmt;
	private int _pmtPid = -1;
	private TransportVideoCodec _videoCodec;

	// 读到 PMT 之前为 -1，节目里没有视频时也是 -1
	public int VideoPid { get; private set; } = -1;

	public NalSyntax? VideoSyntax => _videoCodec switch
	{
		TransportVideoCodec.Avc => NalSyntax.Avc,
		TransportVideoCodec.Hevc => NalSyntax.Hevc,
		TransportVideoCodec.None or TransportVideoCodec.Other => null
	};

	public bool IsComplete => _pat is not null && _pmt is not null;

	public ReadOnlyMemory<byte> Header
	{
		get
		{
			if (field.IsEmpty && _pat is { } pat && _pmt is { } pmt)
			{
				field = (byte[])[.. pat, .. pmt];
			}

			return field;
		}

		private set;
	}

	// 返回这个包是不是 PAT 或 PMT
	public bool Inspect(ReadOnlySpan<byte> packet)
	{
		int pid = TransportStream.GetPid(packet);

		if (pid is 0)
		{
			if (TransportStream.IsPayloadStart(packet) && TransportStream.ReadPmtPid(packet) is var pmtPid and >= 0)
			{
				if (pmtPid != _pmtPid)
				{
					_pmtPid = pmtPid;
					_pmt = null;
				}

				Store(ref _pat, packet);
			}

			return true;
		}

		if (pid != _pmtPid)
		{
			return false;
		}

		if (TransportStream.IsPayloadStart(packet) && TransportStream.TryReadVideoStream(packet, out int videoPid, out TransportVideoCodec codec))
		{
			(VideoPid, _videoCodec) = (videoPid, codec);
			Store(ref _pmt, packet);
		}

		return true;
	}

	// PAT 和 PMT 周期性重发时只有连续计数器在变
	private void Store(ref byte[]? cached, ReadOnlySpan<byte> packet)
	{
		if (cached is not null && cached.AsSpan(0, 3).SequenceEqual(packet.Slice(0, 3)) && (cached[3] & 0xF0) == (packet[3] & 0xF0) && cached.AsSpan(4).SequenceEqual(packet.Slice(4)))
		{
			return;
		}

		cached = packet.ToArray();
		Header = default;
	}
}
