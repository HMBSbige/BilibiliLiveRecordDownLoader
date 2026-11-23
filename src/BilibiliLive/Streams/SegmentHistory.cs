namespace BilibiliLive.Streams;

internal sealed class SegmentHistory(int capacity)
{
	private readonly HashSet<string> _set = new(capacity, StringComparer.Ordinal);
	private readonly Queue<string> _order = new(capacity);

	public bool Contains(string item)
	{
		return _set.Contains(item);
	}

	public void Add(string item)
	{
		if (!_set.Add(item))
		{
			return;
		}

		_order.Enqueue(item);

		if (_order.Count > capacity)
		{
			_set.Remove(_order.Dequeue());
		}
	}
}
