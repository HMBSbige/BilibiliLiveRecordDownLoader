using System.Buffers.Binary;

namespace BilibiliLiveRecordDownLoader.Application.Recording.Containers;

internal static class TransportStream
{
	public const int PacketSize = 188;

	public const byte SyncByte = 0x47;

	public const int NullPid = 0x1FFF;

	// SPS 一般紧跟在访问单元分隔符后面，SEI 再长也不会把它推到 PES 的这么多字节以外
	private const int ParameterSetCapacity = 4096;

	private const byte PatTableId = 0x00;
	private const byte PmtTableId = 0x02;
	private const int CrcSize = 4;

	private static ReadOnlySpan<byte> StartCode => [0, 0, 1];

	// data 开头可以有不到一个包的杂字节：HLS 会话用 MediaProbe.IsTransportStream 认 TS 分片时容许，录像里也原样保留；杂字节里碰巧有 0x47 很常见，开头是 0x47 也要连续三个包都对得上才算
	// 找不到时返回 0
	public static int FindFirstPacket(ReadOnlySpan<byte> data)
	{
		for (int offset = 0; offset < PacketSize && offset + 3 * PacketSize <= data.Length; ++offset)
		{
			if (data[offset] is SyncByte && data[offset + PacketSize] is SyncByte && data[offset + 2 * PacketSize] is SyncByte)
			{
				return offset;
			}
		}

		return 0;
	}

	public static int GetPid(ReadOnlySpan<byte> packet)
	{
		return (packet[1] & 0x1F) << 8 | packet[2];
	}

	public static bool IsPayloadStart(ReadOnlySpan<byte> packet)
	{
		return (packet[1] & 0x40) is not 0;
	}

	public static ReadOnlySpan<byte> GetPayload(ReadOnlySpan<byte> packet)
	{
		return GetPayloadOffset(packet) is var offset and >= 0 ? packet.Slice(offset) : default;
	}

	// 没有负载时返回 -1
	public static int GetPayloadOffset(ReadOnlySpan<byte> packet)
	{
		if ((packet[3] & 0x10) is 0)
		{
			return -1;
		}

		int offset = (packet[3] & 0x20) is 0 ? 4 : 5 + packet[4];
		return offset < PacketSize ? offset : -1;
	}

	// PCR 是适配域标志之后的 33 位基准值、6 位保留和 9 位扩展
	public static bool HasPcr(ReadOnlySpan<byte> packet)
	{
		return (packet[3] & 0x20) is not 0 && packet[4] >= 7 && (packet[5] & 0x10) is not 0;
	}

	public static long ReadPcrBase(ReadOnlySpan<byte> packet)
	{
		return (long)packet[6] << 25 | (long)packet[7] << 17 | (long)packet[8] << 9 | (long)packet[9] << 1 | (long)packet[10] >> 7;
	}

	public static void WritePcrBase(Span<byte> packet, long value)
	{
		packet[6] = (byte)(value >> 25);
		packet[7] = (byte)(value >> 17);
		packet[8] = (byte)(value >> 9);
		packet[9] = (byte)(value >> 1);
		packet[10] = (byte)((int)(value & 1) << 7 | packet[10] & 0x7F);
	}

	// 只认放得进一个包的 PAT，返回第一个节目的 PMT PID
	public static int ReadPmtPid(ReadOnlySpan<byte> packet)
	{
		if (!TryGetSection(packet, PatTableId, out ReadOnlySpan<byte> section))
		{
			return -1;
		}

		for (int offset = 8; offset + 4 <= section.Length - CrcSize; offset += 4)
		{
			// 节目号 0 指向网络信息表
			if (BinaryPrimitives.ReadUInt16BigEndian(section.Slice(offset)) is not 0)
			{
				return (section[offset + 2] & 0x1F) << 8 | section[offset + 3];
			}
		}

		return -1;
	}

	// 只认放得进一个包的 PMT；节目里没有视频时 videoPid 为 -1，视频不是 AVC 或 HEVC 时 syntax 为 null
	public static bool TryReadVideoStream(ReadOnlySpan<byte> packet, out int videoPid, out NalSyntax? syntax)
	{
		videoPid = -1;
		syntax = null;

		if (!TryGetSection(packet, PmtTableId, out ReadOnlySpan<byte> section) || section.Length < 12 + CrcSize)
		{
			return false;
		}

		int end = section.Length - CrcSize;

		for (int offset = 12 + ((section[10] & 0x0F) << 8 | section[11]); offset + 5 <= end;)
		{
			int infoLength = (section[offset + 3] & 0x0F) << 8 | section[offset + 4];

			if (offset + 5 + infoLength > end)
			{
				return false;
			}

			if (IsVideo(section[offset], section.Slice(offset + 5, infoLength), out syntax))
			{
				videoPid = (section[offset + 1] & 0x1F) << 8 | section[offset + 2];
				return true;
			}

			offset += 5 + infoLength;
		}

		return true;
	}

	public static bool StartsWithRandomAccessPicture(ReadOnlySpan<byte> data, int videoPid, NalSyntax? syntax)
	{
		NalScanner scanner = default;
		bool started = false;

		for (int offset = 0; offset + PacketSize <= data.Length; offset += PacketSize)
		{
			ReadOnlySpan<byte> packet = data.Slice(offset, PacketSize);

			if (packet[0] is not SyncByte)
			{
				return false;
			}

			if (GetPid(packet) != videoPid)
			{
				continue;
			}

			ReadOnlySpan<byte> payload = GetPayload(packet);

			if (IsPayloadStart(packet))
			{
				if (started)
				{
					return false;
				}

				if (IsRandomAccess(packet))
				{
					return true;
				}

				if (syntax is not { } nalSyntax || GetPesHeaderLength(payload) is not (>= 0 and var headerLength))
				{
					return false;
				}

				scanner = new NalScanner(nalSyntax);
				started = true;
				payload = payload.Slice(headerLength);
			}

			if (started && scanner.Scan(payload) is { } isRandomAccess)
			{
				return isRandomAccess;
			}
		}

		return false;
	}

	public static bool TryReadDecodeTime(ReadOnlySpan<byte> payload, out long time)
	{
		time = 0;

		if (!TryGetTimestampFields(payload, out int pts, out int dts))
		{
			return false;
		}

		time = ReadTimestamp(payload.Slice(dts >= 0 ? dts : pts));
		return true;
	}

	// 只有 PTS 时 dts 为 -1；PES 头固定的 9 字节之后依次是 5 字节的 PTS 和 DTS，节目流映射、填充流这些流 ID 没有这几个字段
	public static bool TryGetTimestampFields(ReadOnlySpan<byte> payload, out int pts, out int dts)
	{
		pts = dts = -1;

		if (payload.Length < 9 || !payload.StartsWith(StartCode) || payload[3] is 0xBC or 0xBE or 0xBF or 0xF0 or 0xF1 or 0xF2 or 0xF8 or 0xFF || (payload[6] & 0xC0) is not 0x80)
		{
			return false;
		}

		switch (payload[7] >> 6)
		{
			case 2 when payload[8] >= 5 && payload.Length >= 14:
				pts = 9;
				return true;
			case 3 when payload[8] >= 10 && payload.Length >= 19:
				(pts, dts) = (9, 14);
				return true;
			default:
				return false;
		}
	}

	public static long ReadTimestamp(ReadOnlySpan<byte> field)
	{
		return (long)(field[0] >> 1 & 0x07) << 30 | (long)field[1] << 22 | (long)(field[2] >> 1) << 15 | (long)field[3] << 7 | (long)field[4] >> 1;
	}

	public static void WriteTimestamp(Span<byte> field, long value)
	{
		field[0] = (byte)(field[0] & 0xF0 | (int)(value >> 29 & 0x0E) | 1);
		field[1] = (byte)(value >> 22);
		field[2] = (byte)(value >> 14 | 1);
		field[3] = (byte)(value >> 7);
		field[4] = (byte)(value << 1 | 1);
	}

	// data 从包边界开始，在其中第一个视频 PES 的开头找 SPS；SPS 只跟在关键帧前面，找不到或被截断时返回 null
	public static VideoResolution? ReadResolution(ReadOnlySpan<byte> data, int videoPid, NalSyntax syntax)
	{
		Span<byte> pes = stackalloc byte[ParameterSetCapacity];
		int length = CopyFirstPes(data, videoPid, pes);
		return ParameterSets.ReadAnnexB(SkipPesHeader(pes.Slice(0, length)), syntax);
	}

	private static int CopyFirstPes(ReadOnlySpan<byte> data, int videoPid, Span<byte> destination)
	{
		int length = 0;
		bool started = false;

		for (int offset = 0; offset + PacketSize <= data.Length && length < destination.Length; offset += PacketSize)
		{
			ReadOnlySpan<byte> packet = data.Slice(offset, PacketSize);

			if (packet[0] is not SyncByte)
			{
				break;
			}

			if (GetPid(packet) != videoPid)
			{
				continue;
			}

			if (IsPayloadStart(packet))
			{
				if (started)
				{
					break;
				}

				started = true;
			}

			if (started)
			{
				length += Append(GetPayload(packet), destination.Slice(length));
			}
		}

		return length;
	}

	private static ReadOnlySpan<byte> SkipPesHeader(ReadOnlySpan<byte> payload)
	{
		return GetPesHeaderLength(payload) is var length and >= 0 ? payload.Slice(length) : payload;
	}

	private static bool IsRandomAccess(ReadOnlySpan<byte> packet)
	{
		return (packet[3] & 0x20) is not 0 && packet[4] > 0 && (packet[5] & 0x40) is not 0;
	}

	private static int Append(ReadOnlySpan<byte> source, Span<byte> destination)
	{
		int count = Math.Min(source.Length, destination.Length);
		source.Slice(0, count).CopyTo(destination);
		return count;
	}

	// PES 头固定 9 字节：起始码和流 ID、长度、两个标志字节、头数据长度；不是完整的 PES 头时返回 -1
	private static int GetPesHeaderLength(ReadOnlySpan<byte> payload)
	{
		return payload.Length >= 9 && payload.StartsWith(StartCode) && 9 + payload[8] <= payload.Length ? 9 + payload[8] : -1;
	}

	private static bool TryGetSection(ReadOnlySpan<byte> packet, byte tableId, out ReadOnlySpan<byte> section)
	{
		section = default;
		ReadOnlySpan<byte> payload = GetPayload(packet);

		if (payload.IsEmpty || 1 + payload[0] + 3 > payload.Length)
		{
			return false;
		}

		ReadOnlySpan<byte> data = payload.Slice(1 + payload[0]);
		int length = 3 + ((data[1] & 0x0F) << 8 | data[2]);

		if (data[0] != tableId || length > data.Length)
		{
			return false;
		}

		section = data.Slice(0, length);
		return true;
	}

	// MPEG-1/2、MPEG-4 Part 2、VVC、AVS、Dirac、VC-1 和 AV1 也算视频，只是不解析码流；AV1 用私有流类型加 AV01 注册描述符
	private static bool IsVideo(byte streamType, ReadOnlySpan<byte> descriptors, out NalSyntax? syntax)
	{
		syntax = streamType switch
		{
			0x1B => NalSyntax.Avc,
			0x24 => NalSyntax.Hevc,
			_ => null
		};

		return syntax is not null || streamType is 0x01 or 0x02 or 0x10 or 0x33 or 0x42 or 0xD1 or 0xD2 or 0xD4 or 0xEA || streamType is 0x06 && HasRegistration(descriptors, "AV01"u8);

		static bool HasRegistration(ReadOnlySpan<byte> descriptors, ReadOnlySpan<byte> format)
		{
			for (int offset = 0; offset + 2 <= descriptors.Length; offset += 2 + descriptors[offset + 1])
			{
				if (descriptors[offset] is 0x05 && descriptors[offset + 1] >= 4 && offset + 6 <= descriptors.Length && descriptors.Slice(offset + 2, 4).SequenceEqual(format))
				{
					return true;
				}
			}

			return false;
		}
	}

	// 起始码可能跨包，逐字节记住已经连续看到几个 0
	private struct NalScanner(NalSyntax syntax)
	{
		private int _zeros;
		private bool _isAtNalHeader;

		// 还没看到图像 NAL 时返回 null
		public bool? Scan(ReadOnlySpan<byte> data)
		{
			foreach (byte value in data)
			{
				if (_isAtNalHeader)
				{
					_isAtNalHeader = false;

					if (Classify(value) is { } isRandomAccess)
					{
						return isRandomAccess;
					}
				}

				if (value is 0)
				{
					++_zeros;
					continue;
				}

				_isAtNalHeader = value is 1 && _zeros >= 2;
				_zeros = 0;
			}

			return null;
		}

		// H.264 的 1–5 和 H.265 的 0–31 是图像 NAL；其中 H.264 的 IDR 和 H.265 的 IRAP（16–23）可以独立解码
		private readonly bool? Classify(byte header)
		{
			return syntax switch
			{
				NalSyntax.Avc => (header & 0x1F) switch
				{
					5 => true,
					>= 1 and <= 4 => false,
					_ => null
				},
				NalSyntax.Hevc => (header >> 1 & 0x3F) switch
				{
					>= 16 and <= 23 => true,
					< 32 => false,
					_ => null
				}
			};
		}
	}
}
