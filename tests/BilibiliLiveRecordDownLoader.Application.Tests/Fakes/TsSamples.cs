using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

internal static class TsSamples
{
	public const int PacketSize = 188;
	private const int PmtPid = 0x1000;
	public const int VideoPid = 0x100;
	public const int AudioPid = 0x101;
	public const byte AvcStreamType = 0x1B;
	public const byte HevcStreamType = 0x24;
	private const byte AacStreamType = 0x0F;

	// PCR 的 9 位扩展，改写时间戳时要原样保留
	private const int PcrExtension = 0x1AB;

	private const long TimestampPeriod = 1L << 33;

	public static byte[] AvcIdr => [0x65, 0x88, 0x84];

	public static byte[] AvcSlice => [0x41, 0x9A, 0x02];

	public static byte[] HevcIdr => [0x26, 0x01, 0xAF];

	public static byte[] HevcTrail => [0x02, 0x01, 0xD0];

	public static byte[] Pat(int continuity = 0)
	{
		byte[] section = [0x00, 0xB0, 13, 0x00, 0x01, 0xC1, 0x00, 0x00, 0x00, 0x01, 0xE0 | PmtPid >> 8, PmtPid & 0xFF, 0xDE, 0xAD, 0xBE, 0xEF];
		return Packet(0, true, [0, .. section], continuity: continuity);
	}

	public static byte[] Pmt(byte videoStreamType = AvcStreamType, bool hasVideo = true, int continuity = 0, byte version = 0)
	{
		List<byte> streams = [];

		if (hasVideo)
		{
			streams.AddRange([videoStreamType, 0xE0 | VideoPid >> 8, VideoPid & 0xFF, 0xF0, 0x00]);
		}

		streams.AddRange([AacStreamType, 0xE0 | AudioPid >> 8, AudioPid & 0xFF, 0xF0, 0x00]);
		int length = 9 + streams.Count + 4;
		byte[] section = [0x02, (byte)(0xB0 | length >> 8), (byte)length, 0x00, 0x01, (byte)(0xC1 | version << 1), 0x00, 0x00, 0xE1, 0x00, 0xF0, 0x00, .. streams, 0xDE, 0xAD, 0xBE, 0xEF];
		return Packet(PmtPid, true, [0, .. section], continuity: continuity);
	}

	// 不带随机访问标志时，SEI 的长度让图像 NAL 的起始码跨在前两个包之间
	public static byte[] VideoFrame(byte[] picture, bool randomAccess = false, bool isHevc = false)
	{
		List<byte> es = isHevc ? [0, 0, 0, 1, 0x46, 0x01, 0x50, 0, 0, 1, 0x4E, 0x01] : [0, 0, 0, 1, 0x09, 0xF0, 0, 0, 1, 0x06];
		es.AddRange(Enumerable.Repeat((byte)0xAA, PacketSize - 4 - 9 - 1 - es.Count));
		es.AddRange([0, 0, 1, .. picture]);
		byte[] pes = [0, 0, 1, 0xE0, 0, 0, 0x80, 0x00, 0x00, .. es];
		return Pes(VideoPid, pes, randomAccess);
	}

	public static byte[] TimedVideoFrame(byte[] picture, long pts, bool randomAccess = false, byte[]? sps = null, bool isHevc = false, long? dts = null)
	{
		List<byte> es = isHevc ? [0, 0, 0, 1, 0x46, 0x01, 0x50] : [0, 0, 0, 1, 0x09, 0xF0];

		if (sps is not null)
		{
			es.AddRange([0, 0, 0, 1, .. sps]);
		}

		es.AddRange([0, 0, 1, .. picture]);
		byte[] header = new byte[dts is null ? 14 : 19];
		ReadOnlySpan<byte> fixedPart = [0, 0, 1, 0xE0, 0, 0, 0x80, dts is null ? (byte)0x80 : (byte)0xC0, dts is null ? (byte)5 : (byte)10];
		fixedPart.CopyTo(header);
		WriteTimestamp(header.AsSpan(9), dts is null ? 0x20 : 0x30, pts);

		if (dts is { } decode)
		{
			WriteTimestamp(header.AsSpan(14), 0x10, decode);
		}

		return Pes(VideoPid, [.. header, .. es], randomAccess);
	}

	// 带 DTS 时 PTS 和 DTS 的前缀是 0011 和 0001，只有 PTS 时是 0010；PCR 的 6 位保留位都是 1
	public static byte[] TimedPes(int pid, long pts, long? dts = null, long? pcr = null, bool randomAccess = false)
	{
		byte[] packet = new byte[PacketSize];
		packet[0] = 0x47;
		packet[1] = (byte)(0x40 | pid >> 8);
		packet[2] = (byte)pid;
		packet[3] = 0x10;
		int offset = 4;

		if (pcr is not null || randomAccess)
		{
			int length = pcr is null ? 1 : 7;
			packet[3] = 0x30;
			packet[4] = (byte)length;
			packet[5] = (byte)((randomAccess ? 0x40 : 0) | (pcr is null ? 0 : 0x10));

			if (pcr is { } value)
			{
				value &= TimestampPeriod - 1;
				packet[6] = (byte)(value >> 25);
				packet[7] = (byte)(value >> 17);
				packet[8] = (byte)(value >> 9);
				packet[9] = (byte)(value >> 1);
				packet[10] = (byte)((value & 1) << 7 | 0x7E | PcrExtension >> 8);
				packet[11] = PcrExtension & 0xFF;
			}

			offset = 5 + length;
		}

		byte[] header = [0, 0, 1, pid is VideoPid ? (byte)0xE0 : (byte)0xC0, 0, 0, 0x80, dts is null ? (byte)0x80 : (byte)0xC0, dts is null ? (byte)5 : (byte)10];
		header.CopyTo(packet.AsSpan(offset));
		WriteTimestamp(packet.AsSpan(offset + 9), dts is null ? 0x20 : 0x30, pts);

		if (dts is { } decode)
		{
			WriteTimestamp(packet.AsSpan(offset + 14), 0x10, decode);
		}

		return packet;
	}

	public static byte[] Audio(string marker = "")
	{
		byte[] pes = [0, 0, 1, 0xC0, 0, 0, 0x80, 0x00, 0x00, .. Encoding.ASCII.GetBytes($"[{marker}]")];
		return Pes(AudioPid, pes, false);
	}

	public static byte[] Packet(int pid, bool payloadStart, ReadOnlySpan<byte> payload, bool randomAccess = false, int continuity = 0)
	{
		byte[] packet = new byte[PacketSize];
		packet[0] = 0x47;
		packet[1] = (byte)((payloadStart ? 0x40 : 0) | pid >> 8);
		packet[2] = (byte)pid;
		int stuffing = PacketSize - 4 - payload.Length;

		if (stuffing is 0 && !randomAccess)
		{
			packet[3] = (byte)(0x10 | continuity & 0x0F);
			payload.CopyTo(packet.AsSpan(4));
			return packet;
		}

		// 适配字段补齐包长，顺便带上随机访问标志
		packet[3] = (byte)(0x30 | continuity & 0x0F);
		packet[4] = (byte)(stuffing - 1);

		if (stuffing > 1)
		{
			packet[5] = (byte)(randomAccess ? 0x40 : 0x00);
			packet.AsSpan(6, stuffing - 2).Fill(0xFF);
		}

		payload.CopyTo(packet.AsSpan(4 + stuffing));
		return packet;
	}

	// 前缀 4 位之后是 33 位的值，中间夹着标记位
	private static void WriteTimestamp(Span<byte> field, int prefix, long value)
	{
		value &= TimestampPeriod - 1;
		field[0] = (byte)(prefix | (int)(value >> 29 & 0x0E) | 1);
		field[1] = (byte)(value >> 22);
		field[2] = (byte)(value >> 14 | 1);
		field[3] = (byte)(value >> 7);
		field[4] = (byte)(value << 1 | 1);
	}

	private static byte[] Pes(int pid, byte[] pes, bool randomAccess)
	{
		List<byte> packets = [];

		for (int offset = 0, index = 0; offset < pes.Length; ++index)
		{
			int capacity = PacketSize - 4 - (index is 0 && randomAccess ? 2 : 0);
			int length = Math.Min(capacity, pes.Length - offset);
			packets.AddRange(Packet(pid, index is 0, pes.AsSpan(offset, length), index is 0 && randomAccess, index));
			offset += length;
		}

		return [.. packets];
	}
}
