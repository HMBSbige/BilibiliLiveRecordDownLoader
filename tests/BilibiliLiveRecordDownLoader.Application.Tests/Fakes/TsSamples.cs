using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

// 结构完整的 MPEG-TS 包：PAT、PMT 和按 NAL 拼出的视频 PES，CRC 不校验
internal static class TsSamples
{
	public const int PacketSize = 188;
	public const int PmtPid = 0x1000;
	public const int VideoPid = 0x100;
	public const int AudioPid = 0x101;
	public const byte AvcStreamType = 0x1B;
	public const byte HevcStreamType = 0x24;
	public const byte AacStreamType = 0x0F;

	public static byte[] AvcIdr => [0x65, 0x88, 0x84];

	public static byte[] AvcSlice => [0x41, 0x9A, 0x02];

	public static byte[] HevcIdr => [0x26, 0x01, 0xAF];

	public static byte[] HevcTrail => [0x02, 0x01, 0xD0];

	public static byte[] Pat(int pmtPid = PmtPid, int continuity = 0)
	{
		byte[] section = [0x00, 0xB0, 13, 0x00, 0x01, 0xC1, 0x00, 0x00, 0x00, 0x01, (byte)(0xE0 | pmtPid >> 8), (byte)pmtPid, 0xDE, 0xAD, 0xBE, 0xEF];
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

	// 一个访问单元：AUD、SEI，再加上给出的图像 NAL；SEI 的长度让图像 NAL 的起始码跨在第一个包和第二个包之间
	public static byte[] VideoFrame(byte[] picture, bool randomAccess = false, bool isHevc = false)
	{
		List<byte> es = isHevc ? [0, 0, 0, 1, 0x46, 0x01, 0x50, 0, 0, 1, 0x4E, 0x01] : [0, 0, 0, 1, 0x09, 0xF0, 0, 0, 1, 0x06];
		es.AddRange(Enumerable.Repeat((byte)0xAA, PacketSize - 4 - 9 - 1 - es.Count));
		es.AddRange([0, 0, 1, .. picture]);
		byte[] pes = [0, 0, 1, 0xE0, 0, 0, 0x80, 0x00, 0x00, .. es];
		return Pes(VideoPid, pes, randomAccess);
	}

	// 带 PTS 的视频访问单元：AUD、可选的 SPS，再加上图像 NAL
	public static byte[] TimedVideoFrame(byte[] picture, long pts, bool randomAccess = false, byte[]? sps = null, bool isHevc = false)
	{
		List<byte> es = isHevc ? [0, 0, 0, 1, 0x46, 0x01, 0x50] : [0, 0, 0, 1, 0x09, 0xF0];

		if (sps is not null)
		{
			es.AddRange([0, 0, 0, 1, .. sps]);
		}

		es.AddRange([0, 0, 1, .. picture]);
		byte[] header = [0, 0, 1, 0xE0, 0, 0, 0x80, 0x80, 5, (byte)(0x21 | (pts >> 29 & 0x0E)), (byte)(pts >> 22), (byte)(pts >> 14 | 1), (byte)(pts >> 7), (byte)(pts << 1 | 1)];
		return Pes(VideoPid, [.. header, .. es], randomAccess);
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
