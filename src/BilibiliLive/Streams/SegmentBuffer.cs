using System.Buffers;

namespace BilibiliLive.Streams;

internal sealed class SegmentBuffer(int capacity, long? expectedLength = null) : IDisposable
{
	internal const int MinimumCapacity = 4096;

	private byte[] _array = ArrayPool<byte>.Shared.Rent(Math.Max(capacity, MinimumCapacity));
	private int _length;

	public ReadOnlyMemory<byte> Memory => _array.AsMemory(0, _length);

	internal int Capacity => _array.Length;

	public async Task ReadFromAsync(Stream source, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_array.Length is 0, this);

		while (true)
		{
			if (_length == _array.Length)
			{
				// 正文恰好填满数组时先探一个字节，免得为读到结尾再租一块两倍大的
				if (_length != expectedLength)
				{
					Grow();
				}
				else if (await IsAtEndAsync(source, cancellationToken).ConfigureAwait(false))
				{
					return;
				}
			}

			int read = await source.ReadAsync(_array.AsMemory().Slice(_length), cancellationToken).ConfigureAwait(false);

			if (read is 0)
			{
				return;
			}

			_length += read;
		}
	}

	public void Dispose()
	{
		byte[] array = _array;
		_array = [];
		_length = 0;

		if (array.Length > 0)
		{
			ArrayPool<byte>.Shared.Return(array);
		}
	}

	private async Task<bool> IsAtEndAsync(Stream source, CancellationToken cancellationToken)
	{
		byte[] probe = ArrayPool<byte>.Shared.Rent(1);

		try
		{
			if (await source.ReadAsync(probe.AsMemory(0, 1), cancellationToken).ConfigureAwait(false) is 0)
			{
				return true;
			}

			Grow();
			_array[_length++] = probe[0];
			return false;
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(probe);
		}
	}

	private void Grow()
	{
		if (_array.Length >= Array.MaxLength)
		{
			throw new InvalidDataException("The segment is too large.");
		}

		byte[] larger = ArrayPool<byte>.Shared.Rent((int)Math.Min(_array.Length * 2L, Array.MaxLength));
		_array.AsSpan().Slice(0, _length).CopyTo(larger);
		ArrayPool<byte>.Shared.Return(_array);
		_array = larger;
	}
}
