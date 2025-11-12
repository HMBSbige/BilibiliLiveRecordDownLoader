using CryptoBase.Abstractions.Digests;
using CryptoBase.DataFormatExtensions;
using CryptoBase.Digests;
using System.Buffers;
using System.Text;

namespace BilibiliApi.Utils;

internal static class Extensions
{
	public static string ToMd5HexString(this string str)
	{
		byte[] buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(str.Length));

		try
		{
			int length = Encoding.UTF8.GetBytes(str, buffer);

			Span<byte> hash = stackalloc byte[HashConstants.Md5Length];
			using IHash md5 = DigestUtils.Create(DigestType.Md5);
			md5.UpdateFinal(buffer.AsSpan(0, length), hash);

			return hash.ToHex();
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}
}
