using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;

namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// 网络端：会话写出的数据先按单元（从一个切分点到下一个切分点）攒着，写下一个单元之前决定要不要换文件；换文件的位置记成标记，由写盘端照着换
internal sealed partial class RecordingSegmenter : Stream
{
	internal const int DefaultMaxPendingLength = 64 << 20;

	private readonly PipeWriter _output;
	private readonly SplitControl _control;
	private readonly ConcurrentQueue<SegmentStart> _starts;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;
	private readonly PendingBuffer _pending = new();
	private int _scanned;
	private bool _isPendingUnit;
	private bool _isUnitHeld;
	private ReadOnlyMemory<byte> _pendingHeader;
	private DateTimeOffset _pendingTime;
	private int _pendingTitleChange;
	private int _seenTitleChange;
	private long _written;
	private long _segmentLength;
	private bool _segmentHasMedia;
	private DateTimeOffset _segmentStart;

	public RecordingSegmenter(PipeWriter output, MediaSplitter splitter, SplitControl control, ConcurrentQueue<SegmentStart> starts, TimeProvider timeProvider, ILogger logger)
	{
		_output = output;
		Splitter = splitter;
		_control = control;
		_seenTitleChange = _pendingTitleChange = control.TitleChanges;
		_starts = starts;
		_timeProvider = timeProvider;
		_logger = logger;
		_segmentStart = timeProvider.GetUtcNow();
	}

	public MediaSplitter Splitter { get; }

	// 一直等不到下一个切分点时（纯音频、认不出的编码）不能无限攒着，攒到这么多就先写出去
	internal int MaxPendingLength { get; init; } = DefaultMaxPendingLength;

	public override bool CanRead => false;

	public override bool CanSeek => false;

	public override bool CanWrite => true;

	public override long Length => throw new NotSupportedException();

	public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

	public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
	{
		_pending.Append(buffer.Span);
		Process();
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

	// 会话结束后把攒着的数据全部写出，包括末尾不完整的元素；不刷新，缓冲写满时也不能卡在这里
	public void Complete()
	{
		if (_pending.Length > 0)
		{
			Commit(_pending.Length, UnitEnd.StreamEnd);
		}
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
			_pendingTime = _timeProvider.GetUtcNow();

			// 只有这个切分点之前改的标题才在这里切，之后改的等下一个切分点
			_pendingTitleChange = _control.TitleChanges;

			// 不分段时不必等单元收完；单元中途打开分段，从下一个切分点开始判断
			_isUnitHeld = _control.Policy is not { Split: RecordingSplit.None, OnTitleChange: false };
		}

		// 还没到第一个切分点（流头、纯音频）、再也找不到切分点或者不分段时，没有要等的判断，收到就写出
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

	private void Commit(int length, UnitEnd end)
	{
		Span<byte> unit = _pending.Span.Slice(0, length);

		if (_isPendingUnit)
		{
			RecordingSplitPolicy policy = _control.Policy;

			if (Decide(policy, length, end) is { } reason)
			{
				StartSegment(unit, reason);
			}

			if (!_segmentHasMedia && policy.Split is RecordingSplit.BySize { MaxBytes: var maxBytes } && _segmentLength + length > maxBytes)
			{
				LogUnitExceedsSize(_logger, _segmentLength + length, maxBytes);
			}

			_segmentHasMedia = true;
		}

		Splitter.Rebase(unit);
		Emit(unit);
		_segmentLength += length;
		_pending.Discard(length);
	}

	// 每个单元按写出时的设置判断。还没有媒体数据或者没开按标题分段时改了标题都不切，这次请求就此作废。攒不下的单元总长未知，只能保守地从它开头换文件；收流结束时最后半个单元切出去只会得到一个太短的文件，只有放不下才切
	private SplitReason? Decide(RecordingSplitPolicy policy, int unitLength, UnitEnd end)
	{
		bool isTitleChanged = _pendingTitleChange != _seenTitleChange;
		_seenTitleChange = _pendingTitleChange;

		if (!_segmentHasMedia)
		{
			return null;
		}

		return policy.Split switch
		{
			RecordingSplit.BySize when end is UnitEnd.Overflow => SplitReason.Size,
			RecordingSplit.BySize size when _segmentLength + unitLength > size.MaxBytes => SplitReason.Size,
			RecordingSplit.ByDuration duration when end is not UnitEnd.StreamEnd && _pendingTime - _segmentStart >= duration.Duration => SplitReason.Duration,
			_ when isTitleChanged && policy.OnTitleChange && end is not UnitEnd.StreamEnd => SplitReason.Title,
			_ => null
		};
	}

	private void StartSegment(ReadOnlySpan<byte> unit, SplitReason reason)
	{
		_starts.Enqueue(new SegmentStart(_written, _pendingTime, reason));
		Splitter.StartSegment(unit);
		Emit(_pendingHeader.Span);
		_segmentLength = _pendingHeader.Length;
		_segmentHasMedia = false;
		_segmentStart = _pendingTime;
	}

	private void Emit(ReadOnlySpan<byte> data)
	{
		_output.Write(data);
		_written += data.Length;
	}

	// 单元为什么在这里写出：下一个切分点到了，攒不下了，或者收流结束了
	private enum UnitEnd
	{
		NextSplit,
		Overflow,
		StreamEnd
	}

	[LoggerMessage(Level = LogLevel.Warning, Message = "No split point arrived within {Size} bytes, writing them to the current file")]
	private static partial void LogUnitTooLong(ILogger logger, int size);

	[LoggerMessage(Level = LogLevel.Warning, Message = "A keyframe interval of {Size} bytes does not fit within the split size of {MaxSize} bytes and takes a file of its own")]
	private static partial void LogUnitExceedsSize(ILogger logger, long size, long maxSize);
}
