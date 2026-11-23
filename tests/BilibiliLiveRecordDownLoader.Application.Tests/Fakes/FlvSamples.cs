using System.Buffers.Binary;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

// 按 FLV 规范拼出的 tag，只关心切分要看的头几个字节
internal static class FlvSamples
{
	public const byte AudioTag = 8;
	public const byte VideoTag = 9;
	public const byte ScriptTag = 18;

	private const int TagHeaderSize = 11;
	private const int PreviousTagSizeLength = 4;

	// 带音视频标志，DataOffset 为 9，后面跟 PreviousTagSize0
	public static byte[] Header => [.. "FLV"u8, 1, 0x05, 0, 0, 0, 9, 0, 0, 0, 0];

	public static byte[] Metadata(uint timestamp = 0)
	{
		return Tag(ScriptTag, timestamp, [2, 0, 10, .. "onMetaData"u8, .. "meta"u8]);
	}

	// ECMA 数组里 width 和 height 排在字符串、布尔值和嵌套对象后面，确认会跳过不认识的值
	public static byte[] MetadataWithSize(double width, double height)
	{
		List<byte> body = [2, 0, 10, .. "onMetaData"u8, 8, 0, 0, 0, 5];
		Key("encoder");
		body.AddRange([2, 0, 4, .. "test"u8]);
		Key("hasVideo");
		body.AddRange([1, 1]);
		Key("nested");
		body.Add(3);
		Key("x");
		Number(1);
		body.AddRange([0, 0, 9]);
		Key("width");
		Number(width);
		Key("height");
		Number(height);
		body.AddRange([0, 0, 9]);
		return Tag(ScriptTag, 0, [.. body]);

		void Key(string key)
		{
			body.AddRange([0, (byte)key.Length, .. Encoding.ASCII.GetBytes(key)]);
		}

		void Number(double value)
		{
			byte[] bytes = new byte[8];
			BinaryPrimitives.WriteDoubleBigEndian(bytes, value);
			body.AddRange([0, .. bytes]);
		}
	}

	// AVCDecoderConfigurationRecord 带一个 SPS 和一个 PPS
	public static byte[] AvcSequenceHeader(byte[] sps, uint timestamp = 0)
	{
		byte[] record = [1, sps[1], sps[2], sps[3], 0xFF, 0xE1, (byte)(sps.Length >> 8), (byte)sps.Length, .. sps, 1, 0, 4, 0x68, 0xEB, 0xE3, 0xCB];
		return Tag(VideoTag, timestamp, [0x17, 0, 0, 0, 0, .. record]);
	}

	// HEVCDecoderConfigurationRecord 的 22 字节固定部分之后只放一组 SPS；国内扩展用编码 ID 12，Enhanced FLV 用 hvc1
	public static byte[] HevcSequenceHeader(byte[] sps, bool isEnhanced, uint timestamp = 0)
	{
		byte[] fixedPart = new byte[22];
		fixedPart[0] = 1;
		fixedPart[21] = 0x03;
		byte[] record = [.. fixedPart, 1, 0x80 | 33, 0, 1, (byte)(sps.Length >> 8), (byte)sps.Length, .. sps];
		byte[] prefix = isEnhanced ? [0x90, .. "hvc1"u8] : [0x1C, 0, 0, 0, 0];
		return Tag(VideoTag, timestamp, [.. prefix, .. record]);
	}

	public static byte[] Video(VideoStyle style, VideoPacket packet, uint timestamp, string marker = "")
	{
		return Tag(VideoTag, timestamp, VideoBody(style, packet, marker));
	}

	// 补零让文件超过后处理删除的下限
	public static byte[] PaddedVideo(VideoPacket packet, uint timestamp, int padding)
	{
		return Tag(VideoTag, timestamp, [.. VideoBody(VideoStyle.Avc, packet, string.Empty), .. new byte[padding]]);
	}

	private static byte[] VideoBody(VideoStyle style, VideoPacket packet, string marker)
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

		return [.. prefix, .. Encoding.ASCII.GetBytes($"[{marker}]")];
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
		byte[] tag = new byte[TagHeaderSize + body.Length + PreviousTagSizeLength];
		tag[0] = type;
		tag[1] = (byte)(body.Length >> 16);
		tag[2] = (byte)(body.Length >> 8);
		tag[3] = (byte)body.Length;
		tag[4] = (byte)(timestamp >> 16);
		tag[5] = (byte)(timestamp >> 8);
		tag[6] = (byte)timestamp;
		tag[7] = (byte)(timestamp >> 24);
		body.CopyTo(tag.AsSpan(TagHeaderSize));
		BinaryPrimitives.WriteUInt32BigEndian(tag.AsSpan(TagHeaderSize + body.Length), (uint)(TagHeaderSize + body.Length));
		return tag;
	}

	// 每个 tag 的类型、时间戳和标记，例如 "V0[k1]"
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
