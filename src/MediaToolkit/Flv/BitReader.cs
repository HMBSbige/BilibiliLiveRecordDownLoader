namespace MediaToolkit.Flv;

internal ref struct BitReader(ReadOnlySpan<byte> buffer)
{
	private readonly ReadOnlySpan<byte> _buffer = buffer;
	private int _position;

	public bool TryRead(int count, out int value)
	{
		value = 0;

		if (_position + count > _buffer.Length * 8)
		{
			return false;
		}

		for (int i = 0; i < count; ++i, ++_position)
		{
			int bit = _buffer[_position >> 3] >> 7 - (_position & 7) & 1;
			value = value << 1 | bit;
		}

		return true;
	}
}
