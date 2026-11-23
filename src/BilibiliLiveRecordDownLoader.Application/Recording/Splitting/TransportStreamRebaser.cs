namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// PTS、DTS 和 PCR 用同一个起点；所有 PID 一起按每个 PES 的解码时间推进，和 TS 分割器报告给时间轴的媒体时间一一对应
// 回绕只按解码时间展开，起点才和时间轴在同一个展开域里；PTS 和 PCR 按与解码时间的有符号差推出，第一个 PES 的 PTS 和 DTS 落在回绕两边时也不会差一个周期
internal sealed class TransportStreamRebaser
{
	private const long Tolerance = MediaSpan.TimelineRestartToleranceMilliseconds * PtsClock.ClockRate / 1000;

	private PtsClock _unwrap;
	private SegmentClock _clock = new(Tolerance);
	private long? _origin;

	// PCR 按它展开
	private long? _lastDecode;

	public TimeSpan? Origin => _origin is { } origin ? PtsClock.ToTime(origin) : null;

	public void StartSegment()
	{
		_clock = new SegmentClock(Tolerance);
		_origin = null;
	}

	// packets 从包边界开始；失去同步之后找不到边界，不再往后改
	public void Rebase(Span<byte> packets)
	{
		if (_clock.Offset is null && FindOrigin(packets) is { } origin)
		{
			_origin = origin;
			_clock.Begin(origin);
		}

		for (int offset = 0; offset + TransportStream.PacketSize <= packets.Length; offset += TransportStream.PacketSize)
		{
			Span<byte> packet = packets.Slice(offset, TransportStream.PacketSize);

			if (packet[0] is not TransportStream.SyncByte)
			{
				return;
			}

			RebasePes(packet);
			RebasePcr(packet);
		}
	}

	private static long Wrap(long value)
	{
		return Math.Max(value, 0) & PtsClock.Period - 1;
	}

	// 关键帧后面的音频 PES 常早几十毫秒，按第一个 PES 定起点的话它们夹到 0、和下一个 PES 的帧重叠，所以取这一段第一次改写的数据（见 MediaSplitter.HoldsFirstUnit）里最早的解码时间
	// 比第一个 PES 早出容差以上的属于重新开始的时间线，不算
	private long? FindOrigin(ReadOnlySpan<byte> packets)
	{
		// 只是先看一遍，不能动正式展开的状态
		PtsClock unwrap = _unwrap;
		long? first = null;
		long origin = 0;

		for (int offset = 0; offset + TransportStream.PacketSize <= packets.Length; offset += TransportStream.PacketSize)
		{
			ReadOnlySpan<byte> packet = packets.Slice(offset, TransportStream.PacketSize);

			if (packet[0] is not TransportStream.SyncByte)
			{
				break;
			}

			if (!TransportStream.IsPayloadStart(packet) || !TransportStream.TryReadDecodeTime(TransportStream.GetPayload(packet), out long time))
			{
				continue;
			}

			long decode = unwrap.Extend(time);

			if (first is not { } start)
			{
				first = origin = decode;
			}
			else if (decode < origin && decode >= start - Tolerance)
			{
				origin = decode;
			}
		}

		return first is null ? null : origin;
	}

	private void RebasePes(Span<byte> packet)
	{
		if (!TransportStream.IsPayloadStart(packet) || TransportStream.GetPayloadOffset(packet) is not (>= 0 and var start) || !TransportStream.TryGetTimestampFields(packet.Slice(start), out int ptsField, out int dtsField))
		{
			return;
		}

		Span<byte> pts = packet.Slice(start + ptsField, 5);

		if (dtsField < 0)
		{
			long time = _unwrap.Extend(TransportStream.ReadTimestamp(pts));
			_lastDecode = time;
			TransportStream.WriteTimestamp(pts, Wrap(time - _clock.Advance(time, time)));
			return;
		}

		Span<byte> dts = packet.Slice(start + dtsField, 5);
		long decode = _unwrap.Extend(TransportStream.ReadTimestamp(dts));
		long presentation = PtsClock.Near(decode, TransportStream.ReadTimestamp(pts));
		_lastDecode = decode;
		long offset = _clock.Advance(decode, decode);
		TransportStream.WriteTimestamp(pts, Wrap(presentation - offset));
		TransportStream.WriteTimestamp(dts, Wrap(decode - offset));
	}

	// PCR 比 DTS 早一个复用延迟（FFmpeg 的 -muxdelay，命令行默认 0.7 秒），超过判断重新开始的容差，所以不拿它判断，只减去当前的起点
	// 代价是每个文件开头这段 PCR 夹成 0；为了第一帧在 0、和 FLV、fMP4 一致而接受，FFmpeg 系的播放器和转封装不靠 PCR 计时；加 -muxdelay 0 会让 PCR 全程没有余量，更糟
	// 第一个 PES 之前的 PCR 按刚定下的起点展开
	private void RebasePcr(Span<byte> packet)
	{
		if (!TransportStream.HasPcr(packet))
		{
			return;
		}

		long rebased = _clock.Offset is { } offset && (_lastDecode ?? _origin) is { } reference ? Wrap(PtsClock.Near(reference, TransportStream.ReadPcrBase(packet)) - offset) : 0;
		TransportStream.WritePcrBase(packet, rebased);
	}
}
