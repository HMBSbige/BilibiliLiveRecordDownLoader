using Microsoft;

namespace BilibiliApi;

internal class CircleCollection<T>
{
	public int Size { get; }

	public int Count => _queue.Count;

	private readonly Queue<T> _queue;

	public CircleCollection(int size)
	{
		Requires.Range(size > 0, nameof(size));

		Size = size;
		_queue = new Queue<T>(size);
	}

	public bool AddIfNotContains(T item)
	{
		if (_queue.Contains(item))
		{
			return false;
		}

		if (Count >= Size)
		{
			_queue.Dequeue();
		}

		_queue.Enqueue(item);

		return true;
	}
}
