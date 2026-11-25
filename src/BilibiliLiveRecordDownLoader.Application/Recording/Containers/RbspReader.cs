namespace BilibiliLiveRecordDownLoader.Application.Recording.Containers;

// 跳过防竞争字节（00 00 03 里的 03）；读过头之后一直返回 0，由调用方最后检查 IsOverrun
internal ref struct RbspReader(ReadOnlySpan<byte> nal)
{
	private readonly ReadOnlySpan<byte> _data = nal;
	private int _byteOffset;
	private int _bitOffset;
	private int _zeros;
	private byte _current;
	private bool _hasCurrent;

	public bool IsOverrun { get; private set; }

	public uint ReadBits(int count)
	{
		uint value = 0;

		for (int i = 0; i < count; ++i)
		{
			value = value << 1 | ReadBit();
		}

		return value;
	}

	public uint ReadBit()
	{
		if (!_hasCurrent && !TryLoad())
		{
			IsOverrun = true;
			return 0;
		}

		uint bit = (uint)(_current >> 7 - _bitOffset & 1);

		if (++_bitOffset is 8)
		{
			_bitOffset = 0;
			_hasCurrent = false;
		}

		return bit;
	}

	public void Skip(int count)
	{
		for (int i = 0; i < count && !IsOverrun; ++i)
		{
			ReadBit();
		}
	}

	// 无符号指数哥伦布码；前导零超过 31 个时按读过头处理
	public uint ReadUe()
	{
		int leadingZeros = 0;

		while (ReadBit() is 0)
		{
			if (IsOverrun || ++leadingZeros > 31)
			{
				IsOverrun = true;
				return 0;
			}
		}

		return (uint)((1L << leadingZeros) - 1 + ReadBits(leadingZeros));
	}

	public int ReadSe()
	{
		uint value = ReadUe();
		return (value & 1) is 0 ? -(int)(value >> 1) : (int)(value + 1 >> 1);
	}

	private bool TryLoad()
	{
		while (_byteOffset < _data.Length)
		{
			byte value = _data[_byteOffset++];

			if (_zeros >= 2 && value is 3)
			{
				_zeros = 0;
				continue;
			}

			_zeros = value is 0 ? _zeros + 1 : 0;
			_current = value;
			_hasCurrent = true;
			return true;
		}

		return false;
	}
}
