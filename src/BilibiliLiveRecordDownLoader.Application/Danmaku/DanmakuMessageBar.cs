namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

// 成员顺序就是优先级：挤满时数值小的先让位，新条目只能挤走不比自己重要的
internal enum DanmakuMessageKind
{
	Gift,
	Guard,
	SuperChat
}

// Duration 从礼物最后一次到达或其他条目出现时算起；ComboKey 相同且在连击间隔内到达的礼物合成一条
internal readonly record struct DanmakuMessageRequest(TimeSpan Time, DanmakuMessageKind Kind, double Height, TimeSpan Duration, int Count, string? ComboKey);

// From 和 To 是条目底边到消息栏底边的距离；从 Start 起由 From 匀速移到 To，到 MoveEnd 停下；Count 是这一段显示的礼物数量
internal readonly record struct DanmakuMessageSpan(TimeSpan Start, TimeSpan End, double From, double To, TimeSpan MoveEnd, int Count);

// Request 是条目第一次出现时的请求下标
internal sealed record DanmakuMessagePlacement(int Request, TimeSpan ShownAt, TimeSpan FadeOutStart, TimeSpan RemovedAt, IReadOnlyList<DanmakuMessageSpan> Spans);

// Shown 和 Dropped 按请求计，合进连击的也算显示
internal sealed record DanmakuMessageLayout(IReadOnlyList<DanmakuMessagePlacement> Placements, int Shown, int Dropped);

internal static class DanmakuMessageBar
{
	public static readonly TimeSpan Transition = TimeSpan.FromMilliseconds(200);

	public static readonly TimeSpan ComboWindow = TimeSpan.FromSeconds(5);

	private static readonly TimeSpan MinimumDisplay = TimeSpan.FromSeconds(4);

	public static DanmakuMessageLayout Layout(IReadOnlyList<DanmakuMessageRequest> requests, double maxHeight, double gap)
	{
		int[] order = [.. Enumerable.Range(0, requests.Count).OrderBy(i => requests[i].Time)];
		List<Entry> column = [];
		Queue<int> waiting = new();
		List<DanmakuMessagePlacement> placements = [];
		int shown = 0;
		int dropped = 0;
		int next = 0;
		TimeSpan previous = TimeSpan.MinValue;

		while (NextTime(requests, order, next, column, waiting, previous) is { } now)
		{
			previous = now;

			for (int i = column.Count - 1; i >= 0; --i)
			{
				if (column[i].End <= now)
				{
					placements.Add(column[i].Finish(column[i].End - Transition, column[i].End));
					column.RemoveAt(i);
				}
			}

			while (waiting.TryPeek(out int pending) && TryMakeRoom(column, requests[pending], now, maxHeight, gap, placements))
			{
				column.Add(new Entry(pending, requests[pending], now));
				waiting.Dequeue();
				++shown;
			}

			for (; next < order.Length && requests[order[next]].Time <= now; ++next)
			{
				int index = order[next];
				DanmakuMessageRequest request = requests[index];

				if (request.Kind is DanmakuMessageKind.Gift && FindCombo(column, request, now) is { } combo)
				{
					combo.Merge(request, now);
					++shown;
				}
				else if (waiting.Count is 0 && TryMakeRoom(column, request, now, maxHeight, gap, placements))
				{
					column.Add(new Entry(index, request, now));
					++shown;
				}
				else if (request.Kind is DanmakuMessageKind.Gift)
				{
					++dropped;
				}
				else
				{
					waiting.Enqueue(index);
				}
			}

			double offset = 0;

			for (int i = column.Count - 1; i >= 0; --i)
			{
				column[i].MoveTo(offset, now);
				offset += column[i].Height + gap;
			}
		}

		return new DanmakuMessageLayout(placements, shown, dropped);
	}

	private static TimeSpan? NextTime(IReadOnlyList<DanmakuMessageRequest> requests, int[] order, int next, List<Entry> column, Queue<int> waiting, TimeSpan previous)
	{
		TimeSpan? result = next < order.Length ? requests[order[next]].Time : null;

		foreach (Entry entry in column)
		{
			result = Earlier(result, entry.End);

			if (waiting.Count > 0 && entry.EvictableAt > previous)
			{
				result = Earlier(result, entry.EvictableAt);
			}
		}

		return result;

		static TimeSpan Earlier(TimeSpan? current, TimeSpan candidate)
		{
			return current is { } value && value <= candidate ? value : candidate;
		}
	}

	private static Entry? FindCombo(List<Entry> column, DanmakuMessageRequest request, TimeSpan now)
	{
		if (request.ComboKey is null)
		{
			return null;
		}

		for (int i = column.Count - 1; i >= 0; --i)
		{
			if (column[i] is { Kind: DanmakuMessageKind.Gift } entry && string.Equals(entry.ComboKey, request.ComboKey, StringComparison.Ordinal) && now - entry.LastArrival <= ComboWindow)
			{
				return entry;
			}
		}

		return null;
	}

	// 挤走之后确实放得下才挤；空栏总能放下，太高的条目也照样显示
	private static bool TryMakeRoom(List<Entry> column, DanmakuMessageRequest request, TimeSpan now, double maxHeight, double gap, List<DanmakuMessagePlacement> placements)
	{
		double used = column.Sum(entry => entry.Height + gap);

		if (column.Count is 0 || used + request.Height <= maxHeight)
		{
			return true;
		}

		List<Entry> evicted = [];

		foreach (Entry candidate in column.Where(entry => entry.Kind <= request.Kind && entry.EvictableAt <= now).OrderBy(static entry => entry.Kind).ThenBy(static entry => entry.ShownAt))
		{
			evicted.Add(candidate);
			used -= candidate.Height + gap;

			if (used + request.Height <= maxHeight)
			{
				foreach (Entry entry in evicted)
				{
					column.Remove(entry);
					placements.Add(entry.Evict(now));
				}

				return true;
			}
		}

		return false;
	}

	private sealed class Entry(int requestIndex, DanmakuMessageRequest first, TimeSpan shownAt)
	{
		private readonly List<DanmakuMessageSpan> _spans = [];
		private TimeSpan _spanStart = shownAt;
		private double _from = double.NaN;
		private double _to = double.NaN;
		private TimeSpan _moveEnd = shownAt;
		private int _spanCount = first.Count;
		private int _count = first.Count;
		private bool _countChanged;

		public DanmakuMessageKind Kind { get; } = first.Kind;

		public double Height { get; } = first.Height;

		public string? ComboKey { get; } = first.ComboKey;

		public TimeSpan ShownAt { get; } = shownAt;

		public TimeSpan EvictableAt => ShownAt + MinimumDisplay;

		public TimeSpan LastArrival { get; private set; } = shownAt;

		public TimeSpan End { get; private set; } = shownAt + first.Duration;

		public void Merge(DanmakuMessageRequest request, TimeSpan now)
		{
			_count += request.Count;
			LastArrival = now;
			End = now + request.Duration;
			_countChanged = true;
		}

		public void MoveTo(double target, TimeSpan now)
		{
			// 刚显示的这一刻就可能合进了同一时刻到达的连击，第一段要用合并后的数量
			if (double.IsNaN(_to))
			{
				(_from, _to, _moveEnd) = (target, target, now);
				_spanCount = _count;
				_countChanged = false;
				return;
			}

			// 偏移是逐条累加出来的，比较时容忍浮点误差
			bool moved = Math.Abs(target - _to) > 0.01;

			if (!moved && !_countChanged)
			{
				return;
			}

			double position = PositionAt(now);
			Close(now);
			(_from, _to) = (position, target);

			if (moved)
			{
				_moveEnd = now + Transition;
			}
			else if (_moveEnd < now)
			{
				_moveEnd = now;
			}

			_countChanged = false;
		}

		public DanmakuMessagePlacement Finish(TimeSpan fadeOutStart, TimeSpan removedAt)
		{
			Close(removedAt);
			return new DanmakuMessagePlacement(requestIndex, ShownAt, fadeOutStart, removedAt, _spans);
		}

		public DanmakuMessagePlacement Evict(TimeSpan now)
		{
			if (End - Transition <= now)
			{
				return Finish(End - Transition, End);
			}

			double position = PositionAt(now);
			Close(now);
			(_from, _to, _moveEnd) = (position, position, now);
			return Finish(now, now + Transition);
		}

		private double PositionAt(TimeSpan time)
		{
			if (time >= _moveEnd || _moveEnd <= _spanStart)
			{
				return _to;
			}

			return _from + (_to - _from) * ((time - _spanStart) / (_moveEnd - _spanStart));
		}

		private void Close(TimeSpan end)
		{
			if (end > _spanStart)
			{
				_spans.Add(new DanmakuMessageSpan(_spanStart, end, _from, _to, _moveEnd, _spanCount));
			}

			_spanStart = end;
			_spanCount = _count;
		}
	}
}
