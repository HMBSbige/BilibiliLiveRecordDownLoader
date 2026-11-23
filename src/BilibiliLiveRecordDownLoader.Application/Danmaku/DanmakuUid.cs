using CryptoBase.Hashes;
using CryptoBase.Hashes.Crc32;
using System.Buffers.Binary;

namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

internal static class DanmakuUid
{
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

	private static uint Crc32(ReadOnlySpan<byte> data)
	{
		Span<byte> hash = stackalloc byte[HashAlgorithm<Crc32Core>.HashLength];
		HashAlgorithm<Crc32Core>.HashData(data, hash);
		return BinaryPrimitives.ReadUInt32BigEndian(hash);
	}
}
