using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;

namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

internal sealed class SegmentStarts
{
	private readonly ConcurrentQueue<SegmentStart> _queue = new();

	public Writer CreateWriter(PipeWriter output)
	{
		return new Writer(this, output);
	}

	public bool TryTake(long readEnd, out SegmentStart start)
	{
		return _queue.TryPeek(out start) && start.Offset < readEnd && _queue.TryDequeue(out start);
	}

	// 偏移只在这里计；标记按调用时的位置入队，先于之后的数据，写盘端读到数据时一定也看得到标记
	internal sealed class Writer(SegmentStarts starts, PipeWriter output)
	{
		private long _written;

		public void Start(DateTimeOffset startTime, SplitReason reason, int segment)
		{
			starts._queue.Enqueue(new SegmentStart(_written, startTime, reason, segment));
		}

		public void Write(ReadOnlySpan<byte> data)
		{
			output.Write(data);
			_written += data.Length;
		}

		public ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken)
		{
			return output.FlushAsync(cancellationToken);
		}
	}
}
