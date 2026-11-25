using System.Runtime.InteropServices;

namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// 元素指 FLV tag 或 HLS 分片
internal abstract class MediaSplitter
{
	private readonly List<MediaMark> _mediaMarks = [];
	private bool _isAtReportedSplit;

	public ReadOnlyMemory<byte> Header { get; private set; }

	public virtual bool CanSplit => true;

	// 按收到的字节计，即下一个元素在收到的数据里的起点
	public long AcceptedLength { get; private set; }

	public ReadOnlySpan<MediaMark> MediaMarks => CollectionsMarshal.AsSpan(_mediaMarks);

	public VideoResolution? Resolution { get; protected set; }

	// 遇到切分点时停在它前面并返回 true，下次调用从这个元素接着看；看完所有完整的元素时返回 false
	public bool TryFindSplit(ReadOnlySpan<byte> data, ref int position)
	{
		while (position < data.Length)
		{
			ReadOnlySpan<byte> rest = data.Slice(position);
			int length = Measure(rest, out bool isSplit);

			if (length is 0)
			{
				return false;
			}

			if (isSplit && !_isAtReportedSplit)
			{
				// 头取的是这个元素之前的状态，元素本身可能就是新的序列头
				Header = CaptureHeader();
				_isAtReportedSplit = true;
				return true;
			}

			if (length > rest.Length)
			{
				return false;
			}

			_isAtReportedSplit = false;
			AcceptedLength += length;
			Accept(rest.Slice(0, length));
			position += length;
		}

		return false;
	}

	public virtual void StartSegment()
	{
	}

	// 当前段文件时间 0 对应的源时间，由 Rebase 定下；为 null 时时间轴从这一段第一个带媒体时间的元素算起
	public virtual TimeSpan? Origin => null;

	// elements 是完整的元素，放弃解析之后的数据原样跟在后面
	public virtual void Rebase(Span<byte> elements)
	{
	}

	// elements 和 Rebase 的一样；isEnabled 为 false 时也要调用：NAL 长度字段宽度这类状态只能按写出的顺序从提交的数据里跟踪，Measure 看到的可能超前
	public virtual int RemoveFiller(Span<byte> elements, bool isEnabled)
	{
		return elements.Length;
	}

	public void ClearMediaMarks()
	{
		_mediaMarks.Clear();
	}

	// 返回 data 开头的元素的总长度，可能长于 data；还判断不了时返回 0
	// 收流结束时不完整的元素会被丢掉，所以认不出的数据要立即放弃解析、返回 data.Length，不能等
	protected abstract int Measure(ReadOnlySpan<byte> data, out bool isSplit);

	protected abstract void Accept(ReadOnlySpan<byte> element);

	protected abstract ReadOnlyMemory<byte> CaptureHeader();

	// 在 Accept 里调用，粒度和 Rebase 推进时间的相同，一个元素里可以有好几条；只报告源时间轴上的原值
	protected void ReportMediaTime(MediaSpan span)
	{
		_mediaMarks.Add(new MediaMark(AcceptedLength, span, Resolution));
	}
}
