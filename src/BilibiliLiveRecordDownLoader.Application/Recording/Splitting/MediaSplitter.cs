namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// 按容器把会话写出的字节划成完整的元素（FLV tag、TS 包、HLS 分片），找出新段可以从哪个元素开始独立解码，并准备新段开头要补的头
internal abstract class MediaSplitter
{
	private bool _isAtReportedSplit;

	// 从最近一次找到的切分点开始新段时，要先写进新文件的头
	public ReadOnlyMemory<byte> Header { get; private set; }

	// 解析不下去之后再也不会有切分点
	public virtual bool CanSplit => true;

	// 从 position 起逐个看完整的元素：遇到切分点时停在它前面并返回 true，下次调用从这个元素接着看；看完所有完整的元素时返回 false
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
				Header = CaptureHeader(rest);
				_isAtReportedSplit = true;
				return true;
			}

			if (length > rest.Length)
			{
				return false;
			}

			_isAtReportedSplit = false;
			Accept(rest.Slice(0, length));
			position += length;
		}

		return false;
	}

	// 新段从 unit 开始，unit 以切分点元素开头
	public virtual void StartSegment(ReadOnlySpan<byte> unit)
	{
	}

	// 写出之前按当前段调整元素，末尾可能是不完整的元素
	public virtual void Rebase(Span<byte> elements)
	{
	}

	// 写出之前原地去掉元素里的填充数据，返回去掉之后的长度，末尾可能是不完整的元素。关闭时也要调用：单元可能攒了一段才提交，NAL 长度字段的宽度这类状态只能按写出的顺序从提交的数据里跟踪，不能用 Measure 提前看到的
	public virtual int RemoveFiller(Span<byte> elements, bool isEnabled)
	{
		return elements.Length;
	}

	// 返回 data 开头的元素的总长度，可能长于 data；还判断不了时返回 0
	protected abstract int Measure(ReadOnlySpan<byte> data, out bool isSplit);

	// 元素完整地过去之后更新缓存的头
	protected abstract void Accept(ReadOnlySpan<byte> element);

	protected abstract ReadOnlyMemory<byte> CaptureHeader(ReadOnlySpan<byte> element);
}
