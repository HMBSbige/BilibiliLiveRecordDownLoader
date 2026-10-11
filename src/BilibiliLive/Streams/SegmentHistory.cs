namespace BilibiliLive.Streams;

internal sealed class SegmentHistory(int capacity)
{
	private readonly HashSet<string> _set = new(capacity, StringComparer.Ordinal);
	private readonly Queue<string> _order = new(capacity);

	public int Count => _set.Count;

	/// <returns><see langword="false"/> when the item is already remembered.</returns>
	public bool Add(string item)
	{
		if (!_set.Add(item))
		{
			return false;
		}

		_order.Enqueue(item);

		if (_order.Count > capacity)
		{
			_set.Remove(_order.Dequeue());
		}

		return true;
	}
}
