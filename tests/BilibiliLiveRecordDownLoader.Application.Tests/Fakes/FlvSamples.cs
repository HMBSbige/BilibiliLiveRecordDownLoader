using System.Buffers.Binary;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

// 按 FLV 规范拼出的 tag，只关心切分要看的头几个字节
internal static class FlvSamples
{
	public const byte AudioTag = 8;
	public const byte VideoTag = 9;
	public const byte ScriptTag = 18;

	// 带音视频标志，DataOffset 为 9，后面跟 PreviousTagSize0
	public static byte[] Header => [.. "FLV"u8, 1, 0x05, 0, 0, 0, 9, 0, 0, 0, 0];

	public static byte[] Metadata(uint timestamp = 0, string marker = "meta")
	{
		byte[] name = [2, 0, 10, .. "onMetaData"u8];
		return Tag(ScriptTag, timestamp, [.. name, .. Encoding.ASCII.GetBytes(marker)]);
	}

	public static byte[] Video(VideoStyle style, VideoPacket packet, uint timestamp, string marker = "")
	{
		bool isKey = packet is VideoPacket.KeyFrame or VideoPacket.SequenceHeader;
		byte frameType = (byte)(isKey ? 1 : 2);
		byte[] prefix = style switch
		{
			VideoStyle.Avc => [(byte)(frameType << 4 | 7), packet is VideoPacket.SequenceHeader ? (byte)0 : (byte)1, 0, 0, 0],
			VideoStyle.LegacyHevc => [(byte)(frameType << 4 | 12), packet is VideoPacket.SequenceHeader ? (byte)0 : (byte)1, 0, 0, 0],
			// 扩展头：包类型 0 是序列头，1 是带合成时间的帧，3 是不带的
			VideoStyle.EnhancedHevc => [(byte)(0x80 | frameType << 4 | (packet is VideoPacket.SequenceHeader ? 0 : 1)), .. "hvc1"u8, 0, 0, 0],
			_ => [(byte)(0x80 | frameType << 4 | (packet is VideoPacket.SequenceHeader ? 0 : 3)), .. "av01"u8]
		};

		return Tag(VideoTag, timestamp, [.. prefix, .. Encoding.ASCII.GetBytes($"[{marker}]")]);
	}

	public static byte[] AudioSequenceHeader(uint timestamp = 0, string marker = "aac")
	{
		return Tag(AudioTag, timestamp, [0xAF, 0, .. Encoding.ASCII.GetBytes(marker)]);
	}

	public static byte[] Audio(uint timestamp, string marker = "")
	{
		return Tag(AudioTag, timestamp, [0xAF, 1, .. Encoding.ASCII.GetBytes($"[{marker}]")]);
	}

	public static byte[] Tag(byte type, uint timestamp, byte[] body)
	{
		byte[] tag = new byte[11 + body.Length + 4];
		tag[0] = type;
		tag[1] = (byte)(body.Length >> 16);
		tag[2] = (byte)(body.Length >> 8);
		tag[3] = (byte)body.Length;
		tag[4] = (byte)(timestamp >> 16);
		tag[5] = (byte)(timestamp >> 8);
		tag[6] = (byte)timestamp;
		tag[7] = (byte)(timestamp >> 24);
		body.CopyTo(tag.AsSpan(11));
		BinaryPrimitives.WriteUInt32BigEndian(tag.AsSpan(11 + body.Length), (uint)(11 + body.Length));
		return tag;
	}

	// 去掉文件头，按顺序列出每个 tag 的类型、时间戳和标记，例如 "V0[k1]"
	public static IReadOnlyList<string> Describe(byte[] file)
	{
		List<string> tags = [];
		ReadOnlySpan<byte> data = file.AsSpan(13);

		while (data.Length >= 15)
		{
			int size = data[1] << 16 | data[2] << 8 | data[3];
			uint timestamp = (uint)(data[7] << 24 | data[4] << 16 | data[5] << 8 | data[6]);
			string body = Encoding.ASCII.GetString(data.Slice(11, size));
			int marker = body.IndexOf('[', StringComparison.Ordinal);
			string kind = data[0] switch
			{
				AudioTag => "A",
				VideoTag => "V",
				_ => "S"
			};

			tags.Add($"{kind}{timestamp}{(marker >= 0 ? body[marker..] : string.Empty)}");
			data = data.Slice(11 + size + 4);
		}

		return tags;
	}
}

public enum VideoStyle
{
	Avc,
	LegacyHevc,
	EnhancedHevc,
	EnhancedAv1
}

public enum VideoPacket
{
	SequenceHeader,
	KeyFrame,
	InterFrame
}
