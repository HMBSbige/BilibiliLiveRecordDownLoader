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
	private const byte CommandFrame = 5;

	private byte _fileFlags;
	private bool _hasFileHeader;
	private byte[]? _metadata;
	private byte[]? _videoSequence;
	private byte[]? _audioSequence;
	private ReadOnlyMemory<byte> _header;
	private long _rebased;
	private long _unparsableAt = long.MaxValue;
	private long _timestampOffset;
	private long _lastTimestamp;
	private long _filtered;
	private long _removed;
	private bool _hasFilteredFileHeader;
	private bool _isFilterStopped;
	private NalSyntax _sequenceSyntax;
	private int _nalLengthSize;
	private VideoResolution? _sequenceResolution;
	private VideoResolution? _metadataResolution;

	public override bool CanSplit => !IsUnparsable;

	private bool IsUnparsable => _unparsableAt is not long.MaxValue;

	public override void StartSegment(ReadOnlySpan<byte> unit)
	{
		_timestampOffset = ReadTimestamp(unit);
		_lastTimestamp = 0;
	}

	// 第一段保留源时间戳的起点，之后各段从 0 开始；每段都把重新开始的时间线接到已写出的时间之后，和时间轴放弹幕的方式一致。放弃解析之后的数据不再按 tag 看，里面碰巧像 tag 的字节不能改
	public override void Rebase(Span<byte> elements)
	{
		long start = _rebased;
		_rebased += elements.Length;

		// 放弃解析的位置是按收到的字节算的，去掉的填充数据都在它之前
		int end = (int)Math.Clamp(_unparsableAt - _removed - start, 0, elements.Length);

		// 提交总是从元素边界开始，流开头的文件头是第一次提交的第一个元素；文件头无效时已经放弃解析，end 为 0
		int offset = start is 0 && end >= FileHeaderSize ? (int)BinaryPrimitives.ReadUInt32BigEndian(elements.Slice(5)) + PreviousTagSizeLength : 0;

		while (offset + TagHeaderSize <= end && IsTagType(elements[offset]))
		{
			Span<byte> tag = elements.Slice(offset);
			WriteTimestamp(tag, Retime(tag));
			offset += TagHeaderSize + ReadDataSize(tag) + PreviousTagSizeLength;
		}
	}

	// 只删视频 tag 里的填充 NAL，再改 DataSize 和后面的 PreviousTagSize。放弃解析之后的数据不动；不完整的元素只会在收流结束时出现在末尾，之后不再解析
	public override int RemoveFiller(Span<byte> elements, bool isEnabled)
	{
		long start = _filtered;
		_filtered += elements.Length;
		int end = _isFilterStopped ? 0 : (int)Math.Clamp(_unparsableAt - start, 0, elements.Length);
		int read = 0;
		int write = 0;

		while (read < end)
		{
			ReadOnlySpan<byte> rest = elements.Slice(read, end - read);
			int length;

			if (!_hasFilteredFileHeader)
			{
				length = rest.Length >= FileHeaderSize && BinaryPrimitives.ReadUInt32BigEndian(rest.Slice(5)) is var dataOffset and >= FileHeaderSize and <= ushort.MaxValue ? (int)dataOffset + PreviousTagSizeLength : 0;
			}
			else
			{
				length = rest.Length < TagHeaderSize || !IsTagType(rest[0]) ? 0 : TagHeaderSize + ReadDataSize(rest) + PreviousTagSizeLength;
			}

			if (length is 0 || length > rest.Length)
			{
				_isFilterStopped = true;
				break;
			}

			int written = _hasFilteredFileHeader && rest[0] is VideoTag ? FilterVideoTag(elements, read, write, length, isEnabled) : Keep(elements, read, write, length);
			_hasFilteredFileHeader = true;
			read += length;
			write += written;
		}

		write += Keep(elements, read, write, elements.Length - read);
		_removed += elements.Length - write;
		return write;
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
				_sequenceResolution = ReadSequenceResolution(body);
				Resolution = _sequenceResolution ?? _metadataResolution;
				break;
			case AudioTag when IsAudioSequenceHeader(body):
				Store(ref _audioSequence, element);
				break;
			case ScriptTag when IsMetadata(body):
				Store(ref _metadata, element);
				_metadataResolution = FlvMetadata.ReadResolution(body);
				Resolution = _sequenceResolution ?? _metadataResolution;
				break;
			// 序列头和脚本 tag 不是画面：FFmpeg 把序列头当作解码器配置而不输出成帧，中途重发的 onMetaData 时间戳又常是 0，都不能当作文件第一帧
			case VideoTag or AudioTag:
				ReportMediaTime(MediaSpan.At(TimeSpan.FromMilliseconds(ReadTimestamp(element))));
				break;
		}
	}

	// SPS 里的尺寸是实际编码的，比编码器写进 onMetaData 的可靠；AV1 等认不出的编码只能用 onMetaData
	private static VideoResolution? ReadSequenceResolution(ReadOnlySpan<byte> body)
	{
		NalSyntax? syntax = body switch
		{
			[var first, _, _, _, _, ..] when (first & 0x80) is not 0 => GetEnhancedSyntax(body.Slice(1, 4)),
			[var first, ..] when (first & 0x0F) is AvcCodec => NalSyntax.Avc,
			[var first, ..] when (first & 0x0F) is HevcCodec => NalSyntax.Hevc,
			_ => null
		};

		// 传统格式的帧头之后是包类型和 3 字节合成时间，Enhanced FLV 的帧头之后是 FourCC，配置记录都从第 6 字节开始
		return body.Length <= 5 ? null : syntax switch
		{
			NalSyntax.Avc => ParameterSets.ReadAvcConfiguration(body.Slice(5)),
			NalSyntax.Hevc => ParameterSets.ReadHevcConfiguration(body.Slice(5)),
			_ => null
		};
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

	private static int Keep(Span<byte> elements, int read, int write, int length)
	{
		if (read != write)
		{
			elements.Slice(read, length).CopyTo(elements.Slice(write));
		}

		return length;
	}

	private static NalSyntax? GetEnhancedSyntax(ReadOnlySpan<byte> fourCc)
	{
		if (fourCc.SequenceEqual("avc1"u8))
		{
			return NalSyntax.Avc;
		}

		return fourCc.SequenceEqual("hvc1"u8) ? NalSyntax.Hevc : null;
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

	// 比本段已写出的时间戳早一点的夹到 0；时间线重新开始时从已写出的时间接着往后排。
	// 和 Accept 报告媒体时间的口径一样只按音视频帧判断：中途重发的 onMetaData 和序列头时间戳可能是 0，当成时间线重新开始的话后面的帧整体推后，文件里的时间就和时间轴放弹幕的位置对不上了；
	// 早得多的序列头写在已写出的时间上，文件里的时间戳不倒退
	private uint Retime(ReadOnlySpan<byte> tag)
	{
		uint timestamp = ReadTimestamp(tag);
		long output = timestamp - _timestampOffset;
		bool isRestart = output < _lastTimestamp - MediaSpan.TimelineRestartToleranceMilliseconds;

		switch (tag[0])
		{
			case VideoTag or AudioTag when IsSequenceHeader(tag):
			{
				output = isRestart ? _lastTimestamp : output;
				break;
			}
			case VideoTag or AudioTag:
			{
				if (isRestart)
				{
					_timestampOffset = timestamp - _lastTimestamp;
					output = _lastTimestamp;
				}

				_lastTimestamp = Math.Max(_lastTimestamp, output);
				break;
			}
		}

		return (uint)Math.Max(output, 0);
	}

	// 收流结束时末尾的 tag 可能不完整，只看收到的部分
	private static bool IsSequenceHeader(ReadOnlySpan<byte> tag)
	{
		ReadOnlySpan<byte> body = tag.Slice(TagHeaderSize, Math.Min(ReadDataSize(tag), tag.Length - TagHeaderSize));

		return tag[0] switch
		{
			VideoTag => IsVideoSequenceHeader(body),
			AudioTag => IsAudioSequenceHeader(body),
			_ => false
		};
	}

	// 返回 tag 去掉填充之后的长度。序列头不管开关都要记下，打开之后才知道后面的帧怎么拆
	private int FilterVideoTag(Span<byte> elements, int read, int write, int length, bool isEnabled)
	{
		int dataSize = length - TagHeaderSize - PreviousTagSizeLength;
		int nalStart = FindNalUnits(elements.Slice(read + TagHeaderSize, dataSize), out NalSyntax syntax);
		int nalLength = dataSize - nalStart;
		int lengthSize = _nalLengthSize;
		int kept = isEnabled && nalStart > 0 && lengthSize > 0 && syntax == _sequenceSyntax ? FillerNalUnits.Measure(elements.Slice(read + TagHeaderSize + nalStart, nalLength), lengthSize, syntax) : -1;

		if (kept < 0 || kept == nalLength)
		{
			return Keep(elements, read, write, length);
		}

		int prefix = TagHeaderSize + nalStart;
		Keep(elements, read, write, prefix);
		FillerNalUnits.Move(elements, read + prefix, nalLength, write + prefix, kept, lengthSize, syntax);

		Span<byte> tag = elements.Slice(write);
		int newDataSize = nalStart + kept;
		tag[1] = (byte)(newDataSize >> 16);
		tag[2] = (byte)(newDataSize >> 8);
		tag[3] = (byte)newDataSize;
		BinaryPrimitives.WriteUInt32BigEndian(tag.Slice(TagHeaderSize + newDataSize), (uint)(TagHeaderSize + newDataSize));
		return TagHeaderSize + newDataSize + PreviousTagSizeLength;
	}

	// 返回编码帧里 NAL 数据在 tag 正文里的偏移，不是 AVC 或 HEVC 的编码帧时返回 0；遇到序列头时记下 NAL 长度字段的宽度。
	// 传统格式和国内扩展的 HEVC 是帧头、包类型和 3 字节合成时间；Enhanced FLV 的 CodedFrames 带合成时间，CodedFramesX 不带。多轨、ModEx 和命令帧不看。
	// 多轨和 ModEx 里可能装着改了宽度的序列头，认不出的包之后不再按旧的宽度拆，等下一个认得出的序列头
	private int FindNalUnits(ReadOnlySpan<byte> body, out NalSyntax syntax)
	{
		syntax = default;

		if (body.Length < 5)
		{
			return 0;
		}

		byte first = body[0];
		int packetType;
		int frameHeaderSize;

		if ((first & 0x80) is not 0)
		{
			if ((first >> 4 & 0x07) is CommandFrame)
			{
				return 0;
			}

			packetType = first & 0x0F;

			// 多轨和 ModEx 的这个位置不是 FourCC
			if (GetEnhancedSyntax(body.Slice(1, 4)) is not { } enhanced)
			{
				if (packetType is not (1 or 2 or 3 or 4))
				{
					_nalLengthSize = 0;
				}

				return 0;
			}

			syntax = enhanced;
			frameHeaderSize = packetType is 1 ? 8 : 5;
		}
		else
		{
			if (first >> 4 is CommandFrame || (first & 0x0F) is not (AvcCodec or HevcCodec))
			{
				return 0;
			}

			syntax = (first & 0x0F) is AvcCodec ? NalSyntax.Avc : NalSyntax.Hevc;
			packetType = body[1];
			frameHeaderSize = 5;
		}

		switch (packetType)
		{
			case 0:
				(_sequenceSyntax, _nalLengthSize) = (syntax, FillerNalUnits.ReadLengthSize(body.Slice(5), syntax));
				return 0;
			case 1:
			case 3 when (first & 0x80) is not 0:
				return body.Length >= frameHeaderSize ? frameHeaderSize : 0;
			case 2:
			case 4 when (first & 0x80) is not 0:
				// 序列结束和 HDR 元数据不改宽度
				return 0;
			default:
				_nalLengthSize = 0;
				return 0;
		}
	}

	private int GiveUp(ReadOnlySpan<byte> data)
	{
		_unparsableAt = AcceptedLength;
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
