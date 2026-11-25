using System.Buffers.Binary;

namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

internal sealed class FlvSplitter : MediaSplitter
{
	private byte _fileFlags;
	private bool _hasFileHeader;
	private byte[]? _metadata;
	private byte[]? _videoSequence;
	private byte[]? _audioSequence;
	private ReadOnlyMemory<byte> _header;
	private long _rebased;
	private long _unparsableAt = long.MaxValue;
	private SegmentClock _clock = new(MediaSpan.TimelineRestartToleranceMilliseconds);
	private long _filtered;
	private long _removed;
	private NalSyntax _sequenceSyntax;
	private int _nalLengthSize;
	private VideoResolution? _sequenceResolution;
	private VideoResolution? _metadataResolution;

	public override bool CanSplit => !IsUnparsable;

	private bool IsUnparsable => _unparsableAt is not long.MaxValue;

	public override void StartSegment()
	{
		_clock = new SegmentClock(MediaSpan.TimelineRestartToleranceMilliseconds);
	}

	// 放弃解析之后的数据不再按 tag 看，里面碰巧像 tag 的字节不能改
	public override void Rebase(Span<byte> elements)
	{
		long start = _rebased;
		_rebased += elements.Length;

		// 放弃解析的位置是按收到的字节算的，去掉的填充数据都在它之前
		int end = (int)Math.Clamp(_unparsableAt - _removed - start, 0, elements.Length);
		int offset = FindFirstTag(elements, start, end);

		while (Flv.MeasureTag(elements.Slice(offset, end - offset)) is > 0 and var length)
		{
			Span<byte> tag = elements.Slice(offset, length);
			Flv.WriteTimestamp(tag, Retime(tag));
			offset += length;
		}
	}

	public override int RemoveFiller(Span<byte> elements, bool isEnabled)
	{
		long start = _filtered;
		_filtered += elements.Length;
		int end = (int)Math.Clamp(_unparsableAt - start, 0, elements.Length);
		int read = FindFirstTag(elements, start, end);
		int write = read;

		while (Flv.MeasureTag(elements.Slice(read, end - read)) is > 0 and var length)
		{
			write += elements[read] is Flv.VideoTag ? FilterVideoTag(elements, read, write, length, isEnabled) : Compaction.Keep(elements, read, write, length);
			read += length;
		}

		write += Compaction.Keep(elements, read, write, elements.Length - read);
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

		// 签名对不上时马上放弃，不等凑够文件头
		if (!_hasFileHeader)
		{
			if (!Flv.Signature.StartsWith(data.Slice(0, Math.Min(data.Length, Flv.Signature.Length))))
			{
				return GiveUp(data);
			}

			return data.Length < Flv.FileHeaderSize ? 0 : Flv.ReadFirstTagOffset(data) ?? GiveUp(data);
		}

		// 加密的 tag 和认不出的类型之后再也找不到 tag 的边界，只能不再切
		if (!Flv.IsTagType(data[0]))
		{
			return GiveUp(data);
		}

		int length = Flv.MeasureTag(data);

		if (length is 0)
		{
			return 0;
		}

		ReadOnlySpan<byte> body = Flv.GetBody(data);
		isSplit = data[0] is Flv.VideoTag && Flv.IsKeyFrame(body) && (_videoSequence is not null || !Flv.NeedsSequenceHeader(body));
		return length;
	}

	protected override void Accept(ReadOnlySpan<byte> element)
	{
		// 开头就认不出的数据可能不到一个文件头
		if (IsUnparsable)
		{
			return;
		}

		if (!_hasFileHeader)
		{
			_hasFileHeader = true;
			_fileFlags = element[4];
			return;
		}

		ReadOnlySpan<byte> body = Flv.GetBody(element);

		switch (element[0])
		{
			case Flv.VideoTag when Flv.IsVideoSequenceHeader(body):
				Store(ref _videoSequence, element);
				_sequenceResolution = Flv.ReadSequenceResolution(body);
				Resolution = _sequenceResolution ?? _metadataResolution;
				break;
			case Flv.AudioTag when Flv.IsAudioSequenceHeader(body):
				Store(ref _audioSequence, element);
				break;
			case Flv.ScriptTag when Flv.IsMetadata(body):
				Store(ref _metadata, element);
				_metadataResolution = FlvMetadata.ReadResolution(body);
				Resolution = _sequenceResolution ?? _metadataResolution;
				break;
			// 序列头和脚本 tag 不报告：FFmpeg 不把序列头输出成帧，中途重发的 onMetaData 时间戳又常是 0，都不能当作文件第一帧
			case Flv.VideoTag or Flv.AudioTag:
				ReportMediaTime(MediaSpan.At(TimeSpan.FromMilliseconds(Flv.ReadTimestamp(element))));
				break;
		}
	}

	// 版本 1、DataOffset 为 9 的文件头和 PreviousTagSize0，后面是存下的流头 tag
	protected override ReadOnlyMemory<byte> CaptureHeader()
	{
		if (_header.IsEmpty)
		{
			_header = (byte[])[.. Flv.Signature, 1, _fileFlags, 0, 0, 0, Flv.FileHeaderSize, 0, 0, 0, 0, .. _metadata ?? [], .. _videoSequence ?? [], .. _audioSequence ?? []];
		}

		return _header;
	}

	// 流开头的文件头是第一次提交的第一个元素；文件头无效时已经放弃解析，end 为 0
	private static int FindFirstTag(ReadOnlySpan<byte> elements, long start, int end)
	{
		return start is 0 && end >= Flv.FileHeaderSize && Flv.ReadFirstTagOffset(elements) is { } offset ? offset : 0;
	}

	// 和 Accept 一样只按音视频帧推进，否则和时间轴放弹幕的位置对不上
	// 音视频交错时紧跟第一帧的 tag 可能比它早一点，算出负数，夹到 0
	private uint Retime(ReadOnlySpan<byte> tag)
	{
		uint timestamp = Flv.ReadTimestamp(tag);
		long output = tag[0] is Flv.VideoTag or Flv.AudioTag && !Flv.IsSequenceHeader(tag) ? timestamp - _clock.Advance(timestamp, timestamp) : _clock.Place(timestamp);
		return (uint)Math.Max(output, 0);
	}

	// 序列头不管开关都要记下，打开之后才知道后面的帧怎么拆
	private int FilterVideoTag(Span<byte> elements, int read, int write, int length, bool isEnabled)
	{
		int dataSize = length - Flv.TagHeaderSize - Flv.PreviousTagSizeLength;
		int nalStart = FindNalUnits(elements.Slice(read + Flv.TagHeaderSize, dataSize));
		int nalLength = dataSize - nalStart;
		int kept = isEnabled && nalStart > 0 ? FillerNalUnits.Measure(elements.Slice(read + Flv.TagHeaderSize + nalStart, nalLength), _nalLengthSize, _sequenceSyntax) : -1;

		if (kept < 0 || kept == nalLength)
		{
			return Compaction.Keep(elements, read, write, length);
		}

		int prefix = Flv.TagHeaderSize + nalStart;
		Compaction.Keep(elements, read, write, prefix);
		FillerNalUnits.Move(elements, read + prefix, nalLength, write + prefix, _nalLengthSize, _sequenceSyntax);

		Span<byte> tag = elements.Slice(write);
		int newDataSize = nalStart + kept;
		Flv.WriteDataSize(tag, newDataSize);
		BinaryPrimitives.WriteUInt32BigEndian(tag.Slice(Flv.TagHeaderSize + newDataSize), (uint)(Flv.TagHeaderSize + newDataSize));
		return Flv.TagHeaderSize + newDataSize + Flv.PreviousTagSizeLength;
	}

	// 返回 NAL 数据在 tag 正文里的偏移，不是能按记下的宽度拆开的 AVC 或 HEVC 编码帧时返回 0；遇到序列头时记下 NAL 长度字段的宽度
	// 多轨和 ModEx 里可能装着改了宽度的序列头，认不出的包之后不再按旧的宽度拆，等下一个认得出的序列头
	// AV1 不去填充：B 站只在 fMP4 里提供 AV1
	private int FindNalUnits(ReadOnlySpan<byte> body)
	{
		if (body.Length < 5)
		{
			return 0;
		}

		if (Flv.IsCommandFrame(body))
		{
			return 0;
		}

		bool isEnhanced = (body[0] & 0x80) is not 0;
		int packetType = isEnhanced ? body[0] & 0x0F : body[1];

		if (Flv.GetSyntax(body) is not { } syntax)
		{
			// 多轨和 ModEx 的这个位置不是 FourCC
			if (isEnhanced && packetType is not (1 or 2 or 3 or 4))
			{
				_nalLengthSize = 0;
			}

			return 0;
		}

		// 传统格式和国内扩展的 HEVC 是帧头、包类型和 3 字节合成时间；Enhanced FLV 的 CodedFrames 在 FourCC 之后带合成时间，CodedFramesX 不带
		int nalStart = isEnhanced && packetType is 1 ? 8 : 5;

		switch (packetType)
		{
			case 0:
				(_sequenceSyntax, _nalLengthSize) = (syntax, FillerNalUnits.ReadLengthSize(body.Slice(5), syntax));
				return 0;
			case 1:
			case 3 when isEnhanced:
				return body.Length >= nalStart && _nalLengthSize > 0 && syntax == _sequenceSyntax ? nalStart : 0;
			case 2:
			case 4 when isEnhanced:
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

	// 中途重发的内容没变时不必重建头；比较时不看时间戳，存下的副本时间戳清零，拼进头里就是 0
	private void Store(ref byte[]? cached, ReadOnlySpan<byte> tag)
	{
		if (cached is not null && cached.AsSpan(0, 4).SequenceEqual(tag.Slice(0, 4)) && cached.AsSpan(8).SequenceEqual(tag.Slice(8)))
		{
			return;
		}

		cached = tag.ToArray();
		Flv.WriteTimestamp(cached, 0);
		_header = default;
	}
}
