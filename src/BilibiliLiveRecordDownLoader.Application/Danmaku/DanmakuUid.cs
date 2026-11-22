namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

internal static class DanmakuUid
{
	private static readonly uint[] Crc32Table = CreateCrc32Table();

	// B 站弹幕的 p[6] 是 uid 十进制字符串的 CRC32，按 JavaScript 的 toString(16) 输出，不补前导零
	public static string Hash(long uid)
	{
		if (uid is 0)
		{
			return @"0";
		}

		Span<byte> digits = stackalloc byte[20];
		uid.TryFormat(digits, out int length, default, CultureInfo.InvariantCulture);
		return Crc32(digits.Slice(0, length)).ToString(@"x", CultureInfo.InvariantCulture);
	}

	// IEEE 802.3 多项式的反射形式，和 zlib 一致
	internal static uint Crc32(ReadOnlySpan<byte> data)
	{
		uint crc = uint.MaxValue;

		foreach (byte value in data)
		{
			crc = Crc32Table[(byte)(crc ^ value)] ^ (crc >> 8);
		}

		return ~crc;
	}

	private static uint[] CreateCrc32Table()
	{
		uint[] table = new uint[256];

		for (uint i = 0; i < table.Length; ++i)
		{
			uint value = i;

			for (int bit = 0; bit < 8; ++bit)
			{
				value = (value & 1) is 0 ? value >> 1 : (value >> 1) ^ 0xEDB88320;
			}

			table[i] = value;
		}

		return table;
	}
}
