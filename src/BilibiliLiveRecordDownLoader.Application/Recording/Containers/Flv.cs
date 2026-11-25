using System.Buffers.Binary;
using System.Numerics;

namespace BilibiliLiveRecordDownLoader.Application.Recording.Containers;

// 视频 tag 正文的第一个字节：传统格式高 4 位是帧类型、低 4 位是编码 ID；Enhanced FLV 用最高位标记扩展头，帧类型只占 3 位，低 4 位换成包类型，后面跟 FourCC
internal static class Flv
{
	public const int FileHeaderSize = 9;
	public const int TagHeaderSize = 11;
	public const int PreviousTagSizeLength = 4;
	public const byte AudioTag = 8;
	public const byte VideoTag = 9;
	public const byte ScriptTag = 18;

	private const uint MaxDataSize = 0xFF_FFFF;
	private const byte AacFormat = 10;
	private const byte ExAudioFormat = 9;
	private const byte AvcCodec = 7;
	private const byte HevcCodec = 12;
	private const byte KeyFrame = 1;
	private const byte CommandFrame = 5;

	public static ReadOnlySpan<byte> Signature => "FLV"u8;

	// 第一个 tag 在文件里的偏移，跳过 PreviousTagSize0；header 不到文件头长、签名不对或 DataOffset 不合理时返回 null
	public static int? ReadFirstTagOffset(ReadOnlySpan<byte> header)
	{
		return header.Length >= FileHeaderSize && header.StartsWith(Signature) && BinaryPrimitives.ReadUInt32BigEndian(header.Slice(5)) is >= FileHeaderSize and <= ushort.MaxValue and var dataOffset ? (int)dataOffset + PreviousTagSizeLength : null;
	}

	// 连同后面的 PreviousTagSize；data 开头不是完整的 tag 时返回 0
	public static int MeasureTag(ReadOnlySpan<byte> data)
	{
		return data.Length >= TagHeaderSize && IsTagType(data[0]) && TagHeaderSize + ReadDataSize(data) + PreviousTagSizeLength is var length && length <= data.Length ? length : 0;
	}

	public static bool IsTagType(byte type)
	{
		return type is AudioTag or VideoTag or ScriptTag;
	}

	public static ReadOnlySpan<byte> GetBody(ReadOnlySpan<byte> tag)
	{
		return tag.Slice(TagHeaderSize, ReadDataSize(tag));
	}

	public static void WriteDataSize(Span<byte> tag, int size)
	{
		BinaryPrimitives.WriteUInt32BigEndian(tag, (uint)tag[0] << 24 | (uint)size);
	}

	// 低 24 位在前，扩展的高 8 位在后
	public static uint ReadTimestamp(ReadOnlySpan<byte> tag)
	{
		return BitOperations.RotateRight(BinaryPrimitives.ReadUInt32BigEndian(tag.Slice(4)), 8);
	}

	public static void WriteTimestamp(Span<byte> tag, uint timestamp)
	{
		BinaryPrimitives.WriteUInt32BigEndian(tag.Slice(4), BitOperations.RotateLeft(timestamp, 8));
	}

	// 传统格式按编码 ID，Enhanced FLV 按 FourCC；body 至少 5 字节
	public static NalSyntax? GetSyntax(ReadOnlySpan<byte> body)
	{
		if ((body[0] & 0x80) is 0)
		{
			return (body[0] & 0x0F) switch
			{
				AvcCodec => NalSyntax.Avc,
				HevcCodec => NalSyntax.Hevc,
				_ => null
			};
		}

		ReadOnlySpan<byte> fourCc = body.Slice(1, 4);

		if (fourCc.SequenceEqual("avc1"u8))
		{
			return NalSyntax.Avc;
		}

		return fourCc.SequenceEqual("hvc1"u8) ? NalSyntax.Hevc : null;
	}

	public static bool IsKeyFrame(ReadOnlySpan<byte> body)
	{
		return body switch
		{
			[var first, ..] when (first & 0x80) is not 0 => (first >> 4 & 0x07) is KeyFrame && (first & 0x0F) is 1 or 3,
			[var first, var packetType, ..] when (first & 0x0F) is AvcCodec or HevcCodec => first >> 4 is KeyFrame && packetType is 1,
			[var first, ..] => first >> 4 is KeyFrame && (first & 0x0F) is not (AvcCodec or HevcCodec),
			_ => false
		};
	}

	public static bool IsCommandFrame(ReadOnlySpan<byte> body)
	{
		return body is [var first, ..] && ((first & 0x80) is 0 ? first >> 4 : first >> 4 & 0x07) is CommandFrame;
	}

	public static bool IsVideoSequenceHeader(ReadOnlySpan<byte> body)
	{
		return body switch
		{
			[var first, ..] when (first & 0x80) is not 0 => (first & 0x0F) is 0,
			[var first, 0, ..] => (first & 0x0F) is AvcCodec or HevcCodec,
			_ => false
		};
	}

	// 关键帧要跟在视频序列头后面才能解码
	public static bool NeedsSequenceHeader(ReadOnlySpan<byte> body)
	{
		return body is [var first, ..] && ((first & 0x80) is not 0 || (first & 0x0F) is AvcCodec or HevcCodec);
	}

	public static bool IsAudioSequenceHeader(ReadOnlySpan<byte> body)
	{
		return body switch
		{
			[var first, 0, ..] when first >> 4 is AacFormat => true,
			[var first, ..] => first >> 4 is ExAudioFormat && (first & 0x0F) is 0,
			_ => false
		};
	}

	public static bool IsSequenceHeader(ReadOnlySpan<byte> tag)
	{
		ReadOnlySpan<byte> body = GetBody(tag);

		return tag[0] switch
		{
			VideoTag => IsVideoSequenceHeader(body),
			AudioTag => IsAudioSequenceHeader(body),
			_ => false
		};
	}

	// AMF0 字符串 "onMetaData"：类型 2，2 字节长度 10
	public static bool IsMetadata(ReadOnlySpan<byte> body)
	{
		return body is [2, 0, 10, ..] && body.Slice(3).StartsWith("onMetaData"u8);
	}

	// SPS 里的尺寸是实际编码的，比编码器写进 onMetaData 的可靠；AV1 等认不出的编码只能用 onMetaData
	// 传统格式的帧头之后是包类型和 3 字节合成时间，Enhanced FLV 的帧头之后是 FourCC，配置记录都从第 6 字节开始
	public static VideoResolution? ReadSequenceResolution(ReadOnlySpan<byte> body)
	{
		return body.Length <= 5 ? null : GetSyntax(body) switch
		{
			NalSyntax.Avc => ParameterSets.ReadAvcConfiguration(body.Slice(5)),
			NalSyntax.Hevc => ParameterSets.ReadHevcConfiguration(body.Slice(5)),
			_ => null
		};
	}

	// 和录制时 FlvSplitter 给的一致：第一帧之前读得出尺寸的视频序列头优先，其次是 onMetaData；都没有时接着往后找，遇到不完整或认不出的 tag 就停下
	public static VideoResolution? ReadResolution(ReadOnlySpan<byte> data)
	{
		if (ReadFirstTagOffset(data) is not { } offset || offset > data.Length)
		{
			return null;
		}

		VideoResolution? metadata = null;

		while (MeasureTag(data.Slice(offset)) is > 0 and var length)
		{
			ReadOnlySpan<byte> tag = data.Slice(offset);
			ReadOnlySpan<byte> body = GetBody(tag);

			switch (tag[0])
			{
				case VideoTag when IsVideoSequenceHeader(body):
					if (ReadSequenceResolution(body) is { } sequence)
					{
						return sequence;
					}

					break;
				case ScriptTag when IsMetadata(body):
					metadata = FlvMetadata.ReadResolution(body) ?? metadata;
					break;
				case VideoTag or AudioTag when metadata is not null && !IsSequenceHeader(tag):
					return metadata;
			}

			offset += length;
		}

		return metadata;
	}

	private static int ReadDataSize(ReadOnlySpan<byte> tag)
	{
		return (int)(BinaryPrimitives.ReadUInt32BigEndian(tag) & MaxDataSize);
	}
}
