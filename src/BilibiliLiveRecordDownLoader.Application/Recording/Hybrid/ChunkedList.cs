using System.Runtime.InteropServices;

namespace BilibiliLiveRecordDownLoader.Application.Recording.Hybrid;

// 十小时的录制有几百万个样本，分块存放：扩容时不用整段复制，T 不超过 8 字节时每块也不进大对象堆
// 全是默认值的块不分配，读出来是共用的空块：音频轨和没有 B 帧的视频轨，合成偏移都是 0
internal sealed class ChunkedList<T> where T : unmanaged, IEquatable<T>
{
	private const int ChunkShift = 13;
	public const int ChunkSize = 1 << ChunkShift;
	private const int ChunkMask = ChunkSize - 1;

	private static readonly T[] DefaultChunk = new T[ChunkSize];

	private readonly List<T[]?> _chunks = [];

	public int Count { get; private set; }

	public T this[int index]
	{
		get => _chunks[index >> ChunkShift] is { } chunk ? chunk[index & ChunkMask] : default;
		set
		{
			ref T[]? chunk = ref CollectionsMarshal.AsSpan(_chunks)[index >> ChunkShift];

			if (chunk is not null || !value.Equals(default))
			{
				(chunk ??= new T[ChunkSize])[index & ChunkMask] = value;
			}
		}
	}

	public void Add(T item)
	{
		int index = Count & ChunkMask;

		if (index is 0)
		{
			_chunks.Add(null);
		}

		if (!item.Equals(default))
		{
			(CollectionsMarshal.AsSpan(_chunks)[^1] ??= new T[ChunkSize])[index] = item;
		}

		++Count;
	}

	public IEnumerable<ReadOnlyMemory<T>> EnumerateChunks()
	{
		for (int chunk = 0; chunk < _chunks.Count; ++chunk)
		{
			int length = Math.Min(ChunkSize, Count - (chunk << ChunkShift));
			yield return (_chunks[chunk] ?? DefaultChunk).AsMemory(0, length);
		}
	}
}
