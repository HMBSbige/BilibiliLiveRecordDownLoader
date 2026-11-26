using Microsoft.Win32.SafeHandles;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace BilibiliLiveRecordDownLoader.Application.Recording.Hybrid;

// 从 position 起顺序写到文件里；盒子大小和表项数在写完内容后回填，样本表很大时也不用先在内存里拼好
internal sealed class BoxWriter(SafeFileHandle handle, long position) : IDisposable
{
	private const int BufferSize = 1 << 16;

	private readonly byte[] _buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
	private readonly Stack<long> _boxes = new();
	private long _bufferStart = position;
	private int _count;

	public long Position => _bufferStart + _count;

	public void Begin(uint type)
	{
		_boxes.Push(ReserveUInt32());
		WriteUInt32(type);
	}

	public void BeginFull(uint type, byte version, uint flags = 0)
	{
		Begin(type);
		WriteUInt32((uint)version << 24 | flags & 0x00FF_FFFF);
	}

	public void End()
	{
		long start = _boxes.Pop();
		long size = Position - start;

		if (size > uint.MaxValue)
		{
			throw new UnsupportedLayoutException($"a {size}-byte box");
		}

		PatchUInt32(start, (uint)size);
	}

	public long ReserveUInt32()
	{
		long reserved = Position;
		WriteUInt32(0);
		return reserved;
	}

	// 占位是 GetSpan 整块写进缓冲的，回填时要么还在缓冲里，要么已经整块写进文件，不会跨在两边
	public void PatchUInt32(long reserved, uint value)
	{
		if (reserved >= _bufferStart)
		{
			BinaryPrimitives.WriteUInt32BigEndian(_buffer.AsSpan((int)(reserved - _bufferStart)), value);
			return;
		}

		Span<byte> bytes = stackalloc byte[sizeof(uint)];
		BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
		RandomAccess.Write(handle, bytes, reserved);
	}

	public void WriteUInt8(byte value)
	{
		GetSpan(1)[0] = value;
		_count += 1;
	}

	public void WriteUInt16(ushort value)
	{
		BinaryPrimitives.WriteUInt16BigEndian(GetSpan(2), value);
		_count += 2;
	}

	public void WriteUInt32(uint value)
	{
		BinaryPrimitives.WriteUInt32BigEndian(GetSpan(4), value);
		_count += 4;
	}

	public void WriteUInt64(ulong value)
	{
		BinaryPrimitives.WriteUInt64BigEndian(GetSpan(8), value);
		_count += 8;
	}

	public void WriteUInt32Or64(ulong value, bool isLong)
	{
		if (isLong)
		{
			WriteUInt64(value);
		}
		else
		{
			WriteUInt32((uint)value);
		}
	}

	// 样本表有几百万项，整块转成大端
	public void WriteUInt32s(ReadOnlySpan<uint> values)
	{
		while (!values.IsEmpty)
		{
			if (BufferSize - _count < sizeof(uint))
			{
				Flush();
			}

			int length = Math.Min(values.Length, (BufferSize - _count) / sizeof(uint));
			Span<uint> target = MemoryMarshal.Cast<byte, uint>(_buffer.AsSpan(_count, length * sizeof(uint)));

			if (BitConverter.IsLittleEndian)
			{
				BinaryPrimitives.ReverseEndianness(values.Slice(0, length), target);
			}
			else
			{
				values.Slice(0, length).CopyTo(target);
			}

			_count += length * sizeof(uint);
			values = values.Slice(length);
		}
	}

	public void WriteZeros(int count)
	{
		GetSpan(count).Clear();
		_count += count;
	}

	public void Write(ReadOnlySpan<byte> data)
	{
		while (!data.IsEmpty)
		{
			if (_count == BufferSize)
			{
				Flush();
			}

			int length = Math.Min(data.Length, BufferSize - _count);
			data.Slice(0, length).CopyTo(_buffer.AsSpan(_count));
			_count += length;
			data = data.Slice(length);
		}
	}

	public void Flush()
	{
		if (_count is 0)
		{
			return;
		}

		RandomAccess.Write(handle, _buffer.AsSpan(0, _count), _bufferStart);
		_bufferStart += _count;
		_count = 0;
	}

	public void Dispose()
	{
		ArrayPool<byte>.Shared.Return(_buffer);
	}

	private Span<byte> GetSpan(int length)
	{
		if (BufferSize - _count < length)
		{
			Flush();
		}

		return _buffer.AsSpan(_count, length);
	}
}
