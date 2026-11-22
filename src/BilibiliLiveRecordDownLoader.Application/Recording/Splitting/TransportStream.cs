using System.Buffers.Binary;

namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

internal enum TransportVideoCodec
{
	None,
	Avc,
	Hevc,
	Other
}

// MPEG-TS 包头、PAT/PMT 和视频访问单元的最少解析
internal static class TransportStream
{
	public const int PacketSize = 188;

	public const byte SyncByte = 0x47;

	private const byte PatTableId = 0x00;
	private const byte PmtTableId = 0x02;
	private const int CrcSize = 4;

	public static int GetPid(ReadOnlySpan<byte> packet)
	{
		return (packet[1] & 0x1F) << 8 | packet[2];
	}

	public static bool IsPayloadStart(ReadOnlySpan<byte> packet)
	{
		return (packet[1] & 0x40) is not 0;
	}

	public static bool IsRandomAccess(ReadOnlySpan<byte> packet)
	{
		return (packet[3] & 0x20) is not 0 && packet[4] > 0 && (packet[5] & 0x40) is not 0;
	}

	public static ReadOnlySpan<byte> GetPayload(ReadOnlySpan<byte> packet)
	{
		if ((packet[3] & 0x10) is 0)
		{
			return default;
		}

		int offset = (packet[3] & 0x20) is 0 ? 4 : 5 + packet[4];
		return offset < PacketSize ? packet.Slice(offset) : default;
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

	// 只认放得进一个包的 PMT；节目里没有视频时 videoPid 为 -1
	public static bool TryReadVideoStream(ReadOnlySpan<byte> packet, out int videoPid, out TransportVideoCodec codec)
	{
		videoPid = -1;
		codec = TransportVideoCodec.None;

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

			TransportVideoCodec streamCodec = GetVideoCodec(section[offset], section.Slice(offset + 5, infoLength));

			if (streamCodec is not TransportVideoCodec.None)
			{
				videoPid = (section[offset + 1] & 0x1F) << 8 | section[offset + 2];
				codec = streamCodec;
				return true;
			}

			offset += 5 + infoLength;
		}

		return true;
	}

	// 访问单元里第一个图像 NAL 是不是随机访问点；SPS、PPS、SEI 都在它前面
	public static bool StartsWithRandomAccessPicture(ReadOnlySpan<byte> data, int videoPid, TransportVideoCodec codec)
	{
		NalScanner scanner = new(codec);
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

				// PES 头：起始码和流 ID、长度、两个标志字节、头数据长度
				if (codec is not (TransportVideoCodec.Avc or TransportVideoCodec.Hevc) || payload.Length < 9 || !payload.StartsWith((ReadOnlySpan<byte>)[0, 0, 1]) || 9 + payload[8] > payload.Length)
				{
					return false;
				}

				started = true;
				payload = payload.Slice(9 + payload[8]);
			}

			if (started && scanner.Scan(payload) is { } isRandomAccess)
			{
				return isRandomAccess;
			}
		}

		return false;
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

	private static TransportVideoCodec GetVideoCodec(byte streamType, ReadOnlySpan<byte> descriptors)
	{
		return streamType switch
		{
			0x1B => TransportVideoCodec.Avc,
			0x24 => TransportVideoCodec.Hevc,
			// MPEG-1/2、MPEG-4 Part 2、VVC、AVS
			0x01 or 0x02 or 0x10 or 0x33 or 0x42 or 0xD2 => TransportVideoCodec.Other,
			// AV1 用私有流类型加 AV01 注册描述符
			0x06 when HasRegistration(descriptors, "AV01"u8) => TransportVideoCodec.Other,
			_ => TransportVideoCodec.None
		};

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
	private struct NalScanner(TransportVideoCodec codec)
	{
		private int _zeros;
		private bool _isAtNalHeader;

		// 返回第一个图像 NAL 是不是随机访问点，还没看到时返回 null
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
			return codec switch
			{
				TransportVideoCodec.Avc => (header & 0x1F) switch
				{
					5 => true,
					>= 1 and <= 4 => false,
					_ => null
				},
				_ => (header >> 1 & 0x3F) switch
				{
					>= 16 and <= 23 => true,
					< 32 => false,
					_ => null
				}
			};
		}
	}
}
