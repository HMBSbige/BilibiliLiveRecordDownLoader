using System.Buffers;

namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

internal sealed class PendingBuffer : IDisposable
{
	private const int MinimumCapacity = 64 << 10;

	private byte[] _buffer = [];
	private int _start;

	public int Length { get; private set; }

	public Span<byte> Span => _buffer.AsSpan(_start, Length);

	public void Append(ReadOnlySpan<byte> data)
	{
		if (_start + Length + data.Length > _buffer.Length)
		{
			if (Length + data.Length <= _buffer.Length)
			{
				Span.CopyTo(_buffer);
			}
			else
			{
				byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Max(Math.Max(_buffer.Length * 2, MinimumCapacity), Length + data.Length));
				Span.CopyTo(buffer);
				Return();
				_buffer = buffer;
			}

			_start = 0;
		}

		data.CopyTo(_buffer.AsSpan(_start + Length));
		Length += data.Length;
	}

	public void Discard(int count)
	{
		ArgumentOutOfRangeException.ThrowIfGreaterThan(count, Length);

		_start += count;
		Length -= count;

		if (Length is 0)
		{
			_start = 0;
		}
	}

	public void Dispose()
	{
		Return();
		_buffer = [];
		(_start, Length) = (0, 0);
	}

	private void Return()
	{
		if (_buffer.Length > 0)
		{
			ArrayPool<byte>.Shared.Return(_buffer);
		}
	}
}
