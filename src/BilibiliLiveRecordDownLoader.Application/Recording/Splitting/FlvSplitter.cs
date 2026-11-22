using System.Buffers.Binary;

namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// FLV 在视频关键帧 tag 之前切；新段依次补上 FLV 头、PreviousTagSize0、最近的 onMetaData、视频序列头和音频序列头，时间戳从本段第一个关键帧算起
internal sealed class FlvSplitter : MediaSplitter
{
	private const int FileHeaderSize = 9;
	private const int TagHeaderSize = 11;
	private const int PreviousTagSizeLength = 4;
	private const byte AudioTag = 8;
	private const byte VideoTag = 9;
	private const byte ScriptTag = 18;
	private const byte AacFormat = 10;
	private const byte ExAudioFormat = 9;
	private const byte AvcCodec = 7;
	private const byte HevcCodec = 12;
	private const byte KeyFrame = 1;

	// 关键帧前后音视频交错一般只差几百毫秒，倒退得比这还多说明源的时间线重新开始了，比如主播重新推流
	private const long TimelineTolerance = 1000;

	private byte _fileFlags;
	private bool _hasFileHeader;
	private byte[]? _metadata;
	private byte[]? _videoSequence;
	private byte[]? _audioSequence;
	private ReadOnlyMemory<byte> _header;
	private long _accepted;
	private long _rebased;
	private long _unparsableAt = long.MaxValue;
	private bool _isRebasing;
	private long _timestampOffset;
	private long _lastTimestamp;

	public override bool CanSplit => !IsUnparsable;

	private bool IsUnparsable => _unparsableAt is not long.MaxValue;

	public override void StartSegment(ReadOnlySpan<byte> unit)
	{
		_timestampOffset = ReadTimestamp(unit);
		_lastTimestamp = 0;
		_isRebasing = true;
	}

	// 第一段保持原样；之后各段从 0 开始。放弃解析之后的数据不再按 tag 看，里面碰巧像 tag 的字节不能改
	public override void Rebase(Span<byte> elements)
	{
		long start = _rebased;
		_rebased += elements.Length;

		if (!_isRebasing)
		{
			return;
		}

		int end = (int)Math.Clamp(_unparsableAt - start, 0, elements.Length);

		for (int offset = 0; offset + TagHeaderSize <= end && IsTagType(elements[offset]);)
		{
			Span<byte> tag = elements.Slice(offset);
			WriteTimestamp(tag, Retime(tag[0], ReadTimestamp(tag)));
			offset += TagHeaderSize + ReadDataSize(tag) + PreviousTagSizeLength;
		}
	}

	protected override int Measure(ReadOnlySpan<byte> data, out bool isSplit)
	{
		isSplit = false;

		if (IsUnparsable)
		{
			return data.Length;
		}

		if (!_hasFileHeader)
		{
			if (data.Length < FileHeaderSize)
			{
				return 0;
			}

			uint dataOffset = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(5));
			return data.StartsWith("FLV"u8) && dataOffset is >= FileHeaderSize and <= ushort.MaxValue ? (int)dataOffset + PreviousTagSizeLength : GiveUp(data);
		}

		if (data.Length < TagHeaderSize)
		{
			return 0;
		}

		// 加密的 tag 和认不出的类型之后再也找不到 tag 的边界，只能不再切
		if (!IsTagType(data[0]))
		{
			return GiveUp(data);
		}

		int length = TagHeaderSize + ReadDataSize(data) + PreviousTagSizeLength;

		if (data.Length < length)
		{
			return 0;
		}

		ReadOnlySpan<byte> body = GetBody(data);
		isSplit = data[0] is VideoTag && IsKeyFrame(body) && (_videoSequence is not null || !NeedsSequenceHeader(body));
		return length;
	}

	protected override void Accept(ReadOnlySpan<byte> element)
	{
		_accepted += element.Length;

		if (!_hasFileHeader)
		{
			_hasFileHeader = true;
			_fileFlags = element[4];
			return;
		}

		if (IsUnparsable)
		{
			return;
		}

		ReadOnlySpan<byte> body = GetBody(element);

		switch (element[0])
		{
			case VideoTag when IsVideoSequenceHeader(body):
				Store(ref _videoSequence, element);
				break;
			case AudioTag when IsAudioSequenceHeader(body):
				Store(ref _audioSequence, element);
				break;
			case ScriptTag when IsMetadata(body):
				Store(ref _metadata, element);
				break;
		}
	}

	protected override ReadOnlyMemory<byte> CaptureHeader(ReadOnlySpan<byte> element)
	{
		if (!_header.IsEmpty)
		{
			return _header;
		}

		byte[][] tags = [.. new[] { _metadata, _videoSequence, _audioSequence }.OfType<byte[]>()];
		byte[] header = new byte[FileHeaderSize + PreviousTagSizeLength + tags.Sum(static tag => tag.Length)];
		"FLV"u8.CopyTo(header);
		header[3] = 1;
		header[4] = _fileFlags;
		BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(5), FileHeaderSize);
		int offset = FileHeaderSize + PreviousTagSizeLength;

		foreach (byte[] tag in tags)
		{
			Span<byte> copy = header.AsSpan(offset, tag.Length);
			tag.CopyTo(copy);
			WriteTimestamp(copy, 0);
			offset += tag.Length;
		}

		_header = header;
		return _header;
	}

	// 帧类型在高 4 位；Enhanced FLV 用最高位标记扩展头，低 4 位换成包类型，后面跟 FourCC
	private static bool IsKeyFrame(ReadOnlySpan<byte> body)
	{
		return body switch
		{
			[var first, ..] when (first & 0x80) is not 0 => (first >> 4 & 0x07) is KeyFrame && (first & 0x0F) is 1 or 3,
			[var first, var packetType, ..] when (first & 0x0F) is AvcCodec or HevcCodec => first >> 4 is KeyFrame && packetType is 1,
			[var first, ..] => first >> 4 is KeyFrame && (first & 0x0F) is not (AvcCodec or HevcCodec),
			_ => false
		};
	}

	private static bool IsVideoSequenceHeader(ReadOnlySpan<byte> body)
	{
		return body switch
		{
			[var first, ..] when (first & 0x80) is not 0 => (first & 0x0F) is 0,
			[var first, 0, ..] => (first & 0x0F) is AvcCodec or HevcCodec,
			_ => false
		};
	}

	private static bool NeedsSequenceHeader(ReadOnlySpan<byte> body)
	{
		return body is [var first, ..] && ((first & 0x80) is not 0 || (first & 0x0F) is AvcCodec or HevcCodec);
	}

	private static bool IsAudioSequenceHeader(ReadOnlySpan<byte> body)
	{
		return body switch
		{
			[var first, 0, ..] when first >> 4 is AacFormat => true,
			[var first, ..] => first >> 4 is ExAudioFormat && (first & 0x0F) is 0,
			_ => false
		};
	}

	// AMF0 字符串 "onMetaData"
	private static bool IsMetadata(ReadOnlySpan<byte> body)
	{
		return body is [2, _, _, ..] && BinaryPrimitives.ReadUInt16BigEndian(body.Slice(1)) is var length && body.Length >= 3 + length && body.Slice(3, length).SequenceEqual("onMetaData"u8);
	}

	private static bool IsTagType(byte type)
	{
		return type is AudioTag or VideoTag or ScriptTag;
	}

	private static ReadOnlySpan<byte> GetBody(ReadOnlySpan<byte> tag)
	{
		return tag.Slice(TagHeaderSize, ReadDataSize(tag));
	}

	private static int ReadDataSize(ReadOnlySpan<byte> tag)
	{
		return tag[1] << 16 | tag[2] << 8 | tag[3];
	}

	// 低 24 位在前，扩展的高 8 位在后
	private static uint ReadTimestamp(ReadOnlySpan<byte> tag)
	{
		return (uint)(tag[7] << 24 | tag[4] << 16 | tag[5] << 8 | tag[6]);
	}

	private static void WriteTimestamp(Span<byte> tag, uint timestamp)
	{
		tag[4] = (byte)(timestamp >> 16);
		tag[5] = (byte)(timestamp >> 8);
		tag[6] = (byte)timestamp;
		tag[7] = (byte)(timestamp >> 24);
	}

	// 比本段已写出的时间戳早一点的夹到 0；时间线重新开始时从已写出的时间接着往后排。中途重发的 onMetaData 时间戳可能是 0，脚本 tag 不算进时间线
	private uint Retime(byte type, uint timestamp)
	{
		long output = timestamp - _timestampOffset;

		if (type is not ScriptTag)
		{
			if (output < _lastTimestamp - TimelineTolerance)
			{
				_timestampOffset = timestamp - _lastTimestamp;
				output = _lastTimestamp;
			}

			_lastTimestamp = Math.Max(_lastTimestamp, output);
		}

		return (uint)Math.Max(output, 0);
	}

	private int GiveUp(ReadOnlySpan<byte> data)
	{
		_unparsableAt = _accepted;
		return data.Length;
	}

	// 序列头和元数据一般只发一次，中途变了才换；比较时不看时间戳
	private void Store(ref byte[]? cached, ReadOnlySpan<byte> tag)
	{
		if (cached is not null && cached.Length == tag.Length && cached.AsSpan(0, 4).SequenceEqual(tag.Slice(0, 4)) && cached.AsSpan(8).SequenceEqual(tag.Slice(8)))
		{
			return;
		}

		cached = tag.ToArray();
		_header = default;
	}
}
