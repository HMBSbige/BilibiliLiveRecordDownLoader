using System.Runtime.InteropServices;

namespace BilibiliLiveRecordDownLoader.Application.Recording;

// Segment 从 0 开始；Time 从这个文件的第一帧算起，即播放器显示的时间
internal readonly record struct TimelinePosition(int Segment, string Path, TimeSpan Time);

internal interface IRecordingTimeline
{
	// timestamp 取自 TimeProvider.GetTimestamp()；归属还没确定时等到确定，返回 null 表示这一刻没有对应的录制文件
	ValueTask<TimelinePosition?> PlaceAsync(long timestamp, CancellationToken cancellationToken);

	// 开始时流头里读不到就用这一段里第一次读到的；读不到或者不是这次尝试的文件时为 null
	VideoResolution? GetResolution(string path);

	// 到目前为止提交的内容的时长，段关闭之后就是文件的时长；整段都读不到媒体时间或者不是这次尝试的文件时为 null
	TimeSpan? GetDuration(string path);
}

// 弹幕的位置是收到它之前最后到达的元素的文件时间，加上从那个元素到达起经过的时间；连上时一下收到的 GOP 缓存和 HLS 积压会让媒体时间很快追上，不会把弹幕放早
// 网络端（分段器）、写盘端（换文件）和弹幕端各在自己的线程上调用
internal sealed class RecordingTimeline : IRecordingTimeline
{
	// 要大于正常录制时最长的到达间隔：TS HLS 实测分片 3–5 秒，加上半个目标时长的刷新间隔和下载，约 8 秒
	private static readonly TimeSpan MinExtrapolationLimit = TimeSpan.FromSeconds(10);

	// 正常录制时弹幕排队放置不过几秒
	private static readonly TimeSpan Retention = TimeSpan.FromMinutes(2);

	private const int DefaultPruneThreshold = 1024;

	// 只有数据没有新元素时（大的 tag 还在收、认不出的容器）最多每秒记一次，往后推的上限因此最多早算 1 秒
	private static readonly TimeSpan DataRecordInterval = TimeSpan.FromSeconds(1);

	private readonly Lock _lock = new();
	private readonly TimeProvider _timeProvider;
	private readonly long _retention;
	private readonly int _pruneThreshold;

	// 取空闲超时：卡住到空闲超时这次尝试就结束了，再往后推只会把弹幕放到文件最后一帧之后
	// 在这之前恢复时画面接着写进同一个文件，弹幕跟着往后推才不会挤在一起、放早
	private readonly TimeSpan _extrapolationLimit;
	private readonly List<Record> _records = [];
	private readonly List<Segment> _segments = [];

	// 读不到媒体时间时第一个文件从这里按单调时钟算
	private readonly long _created;
	private int _resolvedCount;
	private long _recordedEnd;
	private long _committedEnd;
	private long? _receivingEnd;
	private bool _isClosed;
	private TaskCompletionSource? _changed;

	public RecordingTimeline(string path, TimeProvider timeProvider, TimeSpan idleTimeout) : this(path, timeProvider, idleTimeout, Retention, DefaultPruneThreshold)
	{
	}

	internal RecordingTimeline(string path, TimeProvider timeProvider, TimeSpan idleTimeout, TimeSpan retention, int pruneThreshold)
	{
		_timeProvider = timeProvider;
		_extrapolationLimit = idleTimeout > MinExtrapolationLimit ? idleTimeout : MinExtrapolationLimit;
		_retention = (long)(retention.TotalSeconds * timeProvider.TimestampFrequency);
		_pruneThreshold = pruneThreshold;
		_created = timeProvider.GetTimestamp();
		_segments.Add(new Segment(0, _created) { Path = path });
	}

	internal int RecordCount
	{
		get
		{
			lock (_lock)
			{
				return _records.Count;
			}
		}
	}

	public async ValueTask<TimelinePosition?> PlaceAsync(long timestamp, CancellationToken cancellationToken)
	{
		while (true)
		{
			Task changed;

			lock (_lock)
			{
				if (TryPlaceCore(timestamp, out TimelinePosition? position))
				{
					return position;
				}

				changed = (_changed ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
			}

			await changed.WaitAsync(cancellationToken);
		}
	}

	// 还要等时返回 false；否则 position 和 PlaceAsync 的结果相同
	internal bool TryPlace(long timestamp, out TimelinePosition? position)
	{
		lock (_lock)
		{
			return TryPlaceCore(timestamp, out position);
		}
	}

	public VideoResolution? GetResolution(string path)
	{
		lock (_lock)
		{
			return FindSegment(path)?.Resolution;
		}
	}

	public TimeSpan? GetDuration(string path)
	{
		lock (_lock)
		{
			return FindSegment(path) is { Clock.Offset: not null } segment ? TimeSpan.FromTicks(segment.Clock.Last) : null;
		}
	}

	// 网络端：End 按收到的字节算
	internal void AddMedia(ReadOnlySpan<MediaMark> marks, long arrival)
	{
		if (marks.IsEmpty)
		{
			return;
		}

		lock (_lock)
		{
			foreach (MediaMark mark in marks)
			{
				_records.Add(new Record(mark.End, arrival, mark.Span, mark.Resolution));
				_recordedEnd = mark.End;
			}

			ResolveCommitted();
		}
	}

	// 网络端每次写入之后调用；没有新元素过去时只记下数据还在到达，往后推的上限从这里算
	internal void AddArrival(long acceptedEnd, VideoResolution? resolution, long arrival)
	{
		lock (_lock)
		{
			AddArrivalCore(acceptedEnd, resolution, arrival);
		}
	}

	// 网络端：接下来提交的单元写进新的一段
	internal int StartSegment()
	{
		lock (_lock)
		{
			int index = _segments.Count;
			_segments.Add(new Segment(index, null));
			return index;
		}
	}

	// 网络端：收到的前 committedEnd 个字节已经写进当前这一段；origin 见 MediaSplitter.Origin
	internal void Commit(long committedEnd, TimeSpan? origin = null)
	{
		lock (_lock)
		{
			if (origin is { } value)
			{
				_segments[^1].Clock.Begin(value.Ticks);
			}

			_committedEnd = committedEnd;
			ResolveCommitted();
		}
	}

	// 网络端：攒着的数据都已经提交；之后收到的弹幕不属于这次尝试的任何文件
	internal void EndReceiving()
	{
		long timestamp = _timeProvider.GetTimestamp();

		lock (_lock)
		{
			_receivingEnd ??= timestamp;
			Signal();
		}
	}

	// 写盘端在观察者知道新文件之后调用，这一段的弹幕从这时起才放得进去
	internal void OpenSegment(int segment, string path)
	{
		lock (_lock)
		{
			if (!_isClosed)
			{
				_segments[segment].Path = path;
				Signal();
			}
		}
	}

	// 写盘端不会再建文件；没建出文件的段和没提交的记录都放不进去了
	internal void Close()
	{
		lock (_lock)
		{
			_isClosed = true;
			Signal();
		}
	}

	private bool TryPlaceCore(long timestamp, out TimelinePosition? position)
	{
		position = null;

		if (timestamp < _created || timestamp > _receivingEnd)
		{
			return true;
		}

		int index = Math.Max(FindLatest(timestamp), 0);

		if (index >= _records.Count)
		{
			return _isClosed || _receivingEnd is not null;
		}

		Record record = _records[index];

		if (record.SegmentIndex < 0 || _segments[record.SegmentIndex].Path is not { } path)
		{
			return _isClosed;
		}

		TimeSpan limit = _timeProvider.GetElapsedTime(record.BaseArrival, record.Arrival) + _extrapolationLimit;
		TimeSpan elapsed = _timeProvider.GetElapsedTime(record.BaseArrival, timestamp);
		position = new TimelinePosition(record.SegmentIndex, path, record.BaseTime + TimeSpan.FromTicks(Math.Clamp(elapsed.Ticks, 0, limit.Ticks)));
		return true;
	}

	private Segment? FindSegment(string path)
	{
		return _segments.Find(segment => string.Equals(segment.Path, path, StringComparison.Ordinal));
	}

	// 到达时刻不晚于 timestamp 的最后一条记录，没有时返回 -1；同一次写入的记录到达时刻相同，取最后一条
	private int FindLatest(long timestamp)
	{
		int low = 0;
		int high = _records.Count;

		while (low < high)
		{
			int middle = low + (high - low) / 2;

			if (_records[middle].Arrival <= timestamp)
			{
				low = middle + 1;
			}
			else
			{
				high = middle;
			}
		}

		return low - 1;
	}

	// 只有数据的记录沿用上一条的段和文件时间，只把到达时刻往后挪；不改已有的记录，晚到的查询仍按当时的情况算上限
	private void AddArrivalCore(long acceptedEnd, VideoResolution? resolution, long arrival)
	{
		if (acceptedEnd <= _recordedEnd && (_records.Count is 0 || _timeProvider.GetElapsedTime(_records[^1].Arrival, arrival) < DataRecordInterval))
		{
			return;
		}

		_records.Add(new Record(Math.Max(acceptedEnd, _recordedEnd), arrival, null, resolution));
		_recordedEnd = Math.Max(acceptedEnd, _recordedEnd);
		ResolveCommitted();
	}

	private void ResolveCommitted()
	{
		Span<Record> records = CollectionsMarshal.AsSpan(_records);
		int resolved = _resolvedCount;

		while (_resolvedCount < records.Length && records[_resolvedCount].End <= _committedEnd)
		{
			Resolve(ref records[_resolvedCount++]);
		}

		if (_resolvedCount != resolved)
		{
			Prune(resolved);
			Signal();
		}
	}

	// 第一个带媒体时间的元素之前（流头）和整段都读不到媒体时间时，按单调时钟从这一段开始算
	private void Resolve(ref Record record)
	{
		Segment segment = _segments[^1];
		segment.Resolution ??= record.Resolution;

		if (segment.Start is null)
		{
			segment.Start = record.Arrival;
			segment.BaseArrival = record.Arrival;
		}

		if (record.Span is { } span)
		{
			// 音视频交错和 B 帧让时间小幅来回，只往前走
			segment.Clock.Advance(span.Start.Ticks, span.End.Ticks);
			segment.BaseTime = TimeSpan.FromTicks(segment.Clock.Last);
			segment.BaseArrival = record.Arrival;
		}

		record.SegmentIndex = segment.Index;
		record.BaseTime = segment.BaseTime;
		record.BaseArrival = segment.BaseArrival;
	}

	// 早于留下的记录太久的弹幕按最早的一条放
	// 保留范围起点上的查询要用的那条也留下：卡流比保留时长还久时，卡流期间收到的弹幕都从卡流前最后一条往后推
	// 这次才确定的记录可能正有弹幕在等，留到之后的提交再剪
	private void Prune(int resolvedBefore)
	{
		int count = Math.Min(FindLatest(_records[_resolvedCount - 1].Arrival - _retention), resolvedBefore);

		if (count >= _pruneThreshold)
		{
			_records.RemoveRange(0, count);
			_resolvedCount -= count;
		}
	}

	private void Signal()
	{
		_changed?.TrySetResult();
		_changed = null;
	}

	private struct Record(long end, long arrival, MediaSpan? span, VideoResolution? resolution)
	{
		public long End { get; } = end;

		public long Arrival { get; } = arrival;

		public MediaSpan? Span { get; } = span;

		public VideoResolution? Resolution { get; } = resolution;

		// 还没提交时为 -1
		public int SegmentIndex { get; set; } = -1;

		// 这条记录到达时这一段最新的文件时间和它对应的到达时刻
		public TimeSpan BaseTime { get; set; }

		public long BaseArrival { get; set; }
	}

	private sealed class Segment(int index, long? start)
	{
		public int Index { get; } = index;

		// 写盘端建好文件之前为 null
		public string? Path { get; set; }

		public VideoResolution? Resolution { get; set; }

		// 这一段第一个元素到达的时刻，第一段是文件建好的时刻
		public long? Start { get; set; } = start;

		// 按 TimeSpan 的刻度计时；可变结构体，用字段才能原地推进
		public SegmentClock Clock = new(MediaSpan.TimelineRestartTolerance.Ticks);

		public TimeSpan BaseTime { get; set; }

		public long BaseArrival { get; set; } = start ?? 0;
	}
}
