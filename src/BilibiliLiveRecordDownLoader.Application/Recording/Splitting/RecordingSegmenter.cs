using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;

namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// 网络端：数据按单元（相邻两个切分点之间）攒着，写出前决定要不要换文件；换文件的位置记成标记，由写盘端照着换
internal sealed partial class RecordingSegmenter : Stream
{
	internal const int DefaultMaxPendingLength = 64 << 20;

	private readonly PipeWriter _output;
	private readonly SplitControl _control;
	private readonly RecordingTimeline _timeline;
	private readonly ConcurrentQueue<SegmentStart> _starts;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;
	private readonly PendingBuffer _pending = new();
	private int _scanned;
	private bool _isPendingUnit;
	private bool _isUnitHeld;
	private ReadOnlyMemory<byte> _pendingHeader;
	private DateTimeOffset _pendingTime;
	private long _pendingTimestamp;
	private int _pendingTitleChange;
	private int _seenTitleChange;
	private long _written;
	private long _committed;
	private long _arrival;
	private long _segmentLength;
	private bool _segmentHasMedia;
	private long _segmentStart;

	public RecordingSegmenter(PipeWriter output, MediaSplitter splitter, SplitControl control, ConcurrentQueue<SegmentStart> starts, TimeProvider timeProvider, ILogger logger, RecordingTimeline timeline)
	{
		_output = output;
		Splitter = splitter;
		_timeline = timeline;
		_control = control;
		_seenTitleChange = _pendingTitleChange = control.TitleChanges;
		_starts = starts;
		_timeProvider = timeProvider;
		_logger = logger;
		_segmentStart = timeProvider.GetTimestamp();
	}

	public MediaSplitter Splitter { get; }

	// 等不到下一个切分点时（纯音频、认不出的编码）攒到这么多就先写出去
	internal int MaxPendingLength { get; init; } = DefaultMaxPendingLength;

	public override bool CanRead => false;

	public override bool CanSeek => false;

	public override bool CanWrite => true;

	public override long Length => throw new NotSupportedException();

	public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

	public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
	{
		_arrival = _timeProvider.GetTimestamp();
		_pending.Append(buffer.Span);
		Process();
		TakeMediaMarks();
		_timeline.AddArrival(Splitter.AcceptedLength, Splitter.Resolution, _arrival);
		await _output.FlushAsync(cancellationToken);
	}

	public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
	{
		return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
	}

	public override void Write(byte[] buffer, int offset, int count)
	{
		throw new NotSupportedException();
	}

	// 末尾不完整的元素丢掉，分割器放弃解析之后的数据照样全部写出；不刷新，缓冲写满时也不能卡在这里
	public void Complete()
	{
		if (_scanned > 0)
		{
			Commit(_scanned, UnitEnd.StreamEnd);
			_scanned = 0;
		}

		if (_pending.Length > 0)
		{
			LogIncompleteTail(_logger, _pending.Length);
		}

		_timeline.EndReceiving();
	}

	public override void Flush()
	{
	}

	public override int Read(byte[] buffer, int offset, int count)
	{
		throw new NotSupportedException();
	}

	public override long Seek(long offset, SeekOrigin origin)
	{
		throw new NotSupportedException();
	}

	public override void SetLength(long value)
	{
		throw new NotSupportedException();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_pending.Dispose();
		}

		base.Dispose(disposing);
	}

	private void Process()
	{
		while (Splitter.TryFindSplit(_pending.Span, ref _scanned))
		{
			if (_scanned > 0)
			{
				Commit(_scanned, UnitEnd.NextSplit);
			}

			_scanned = 0;
			_isPendingUnit = true;
			_pendingHeader = Splitter.Header;
			// 墙钟给文件命名，按时长分段用单调时钟，改系统时间不影响
			_pendingTime = _timeProvider.GetUtcNow();
			_pendingTimestamp = _timeProvider.GetTimestamp();

			// 这个切分点之后改的标题等下一个切分点
			_pendingTitleChange = _control.TitleChanges;

			// 不分段时只攒分割器定起点要的第一个单元；单元中途打开分段，从下一个切分点开始判断
			_isUnitHeld = (Splitter.HoldsFirstUnit && !_segmentHasMedia) || _control.Policy is not { Split: RecordingSplit.None, OnTitleChange: false };
		}

		if (_scanned > 0 && (!_isPendingUnit || !Splitter.CanSplit || !_isUnitHeld))
		{
			Commit(_scanned, UnitEnd.NextSplit);
			_scanned = 0;
			_isPendingUnit = false;
		}
		else if (_scanned >= MaxPendingLength)
		{
			LogUnitTooLong(_logger, MaxPendingLength);
			Commit(_scanned, UnitEnd.Overflow);
			_scanned = 0;

			// 后面的数据接着这个单元，不能从这里切
			_isPendingUnit = false;
		}
	}

	// 按大小分段看的是去掉填充数据之后实际写出的长度
	private void Commit(int length, UnitEnd end)
	{
		// 单元里的元素要先记进时间轴，才能随单元一起归到这一段
		TakeMediaMarks();
		Span<byte> received = _pending.Span.Slice(0, length);
		Span<byte> unit = received.Slice(0, Splitter.RemoveFiller(received, _control.RemoveFiller));

		if (_isPendingUnit)
		{
			RecordingSplitPolicy policy = _control.Policy;

			if (Decide(policy, unit.Length, end) is { } reason)
			{
				StartSegment(reason);
			}

			if (!_segmentHasMedia && policy.Split is RecordingSplit.BySize { MaxBytes: var maxBytes } && _segmentLength + unit.Length > maxBytes)
			{
				LogUnitExceedsSize(_logger, _segmentLength + unit.Length, maxBytes);
			}

			_segmentHasMedia = true;
		}

		Splitter.Rebase(unit);
		Emit(unit);
		_segmentLength += unit.Length;
		_pending.Discard(length);
		_committed += length;
		_timeline.Commit(_committed, Splitter.Origin);
	}

	private void TakeMediaMarks()
	{
		_timeline.AddMedia(Splitter.MediaMarks, _arrival);
		Splitter.ClearMediaMarks();
	}

	// 还没有媒体数据或者没开按标题分段时，改标题的请求就此作废；攒不下的单元总长未知，只能保守地从它开头换文件
	// 收流结束时把最后半个单元切出去只会得到一个太短的文件，只有放不下才切
	private SplitReason? Decide(RecordingSplitPolicy policy, int unitLength, UnitEnd end)
	{
		bool isTitleChanged = _pendingTitleChange != _seenTitleChange;
		_seenTitleChange = _pendingTitleChange;

		if (!_segmentHasMedia)
		{
			return null;
		}

		SplitReason? reason = policy.Split switch
		{
			RecordingSplit.BySize when end is UnitEnd.Overflow => SplitReason.Size,
			RecordingSplit.BySize size when _segmentLength + unitLength > size.MaxBytes => SplitReason.Size,
			RecordingSplit.ByDuration duration when end is not UnitEnd.StreamEnd && _timeProvider.GetElapsedTime(_segmentStart, _pendingTimestamp) >= duration.Duration => SplitReason.Duration,
			RecordingSplit.None or RecordingSplit.BySize or RecordingSplit.ByDuration => null
		};

		return reason ?? (isTitleChanged && policy.OnTitleChange && end is not UnitEnd.StreamEnd ? SplitReason.Title : null);
	}

	private void StartSegment(SplitReason reason)
	{
		_starts.Enqueue(new SegmentStart(_written, _pendingTime, reason, _timeline.StartSegment()));
		Splitter.StartSegment();
		Emit(_pendingHeader.Span);
		_segmentLength = _pendingHeader.Length;
		_segmentHasMedia = false;
		_segmentStart = _pendingTimestamp;
	}

	private void Emit(ReadOnlySpan<byte> data)
	{
		_output.Write(data);
		_written += data.Length;
	}

	private enum UnitEnd
	{
		NextSplit,
		Overflow,
		StreamEnd
	}

	[LoggerMessage(Level = LogLevel.Warning, Message = "No split point arrived within {Size} bytes, writing them to the current file")]
	private static partial void LogUnitTooLong(ILogger logger, int size);

	[LoggerMessage(Level = LogLevel.Debug, Message = "Dropped {Size} bytes of an incomplete element at the end of the stream")]
	private static partial void LogIncompleteTail(ILogger logger, int size);

	[LoggerMessage(Level = LogLevel.Warning, Message = "A keyframe interval of {Size} bytes does not fit within the split size of {MaxSize} bytes and takes a file of its own")]
	private static partial void LogUnitExceedsSize(ILogger logger, long size, long maxSize);
}
