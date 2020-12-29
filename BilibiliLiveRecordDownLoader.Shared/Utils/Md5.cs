using System;
using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace BilibiliLiveRecordDownLoader.Shared.Utils
{
	public static class Md5
	{
		private const byte Md5Len = 16;
		private const byte Md5StrLen = Md5Len * 2;
		private const string Alphabet = @"0123456789abcdef";

		private static readonly ThreadLocal<MD5> Hasher = new(MD5.Create);
		private static readonly ArrayPool<byte> ArrayPool = ArrayPool<byte>.Shared;
		private static readonly Encoding Encoding = Encoding.UTF8;

		public static Span<byte> ComputeHash(in ReadOnlySpan<byte> origin)
		{
			Span<byte> hash = new byte[Md5Len];

			Hasher.Value!.TryComputeHash(origin, hash, out _);

			return hash;
		}

		public static string ComputeHash(in string str)
		{
			var buffer = ArrayPool.Rent(Encoding.GetMaxByteCount(str.Length));
			try
			{
				var length = Encoding.GetBytes(str, buffer);

				Span<byte> hash = stackalloc byte[Md5Len];

				Hasher.Value!.TryComputeHash(buffer.AsSpan(0, length), hash, out _);

				return ToHex(hash);
			}
			finally
			{
				ArrayPool.Return(buffer);
			}
		}

		private static string ToHex(in ReadOnlySpan<byte> bytes)
		{
			Span<char> c = stackalloc char[Md5StrLen];

			var i = 0;
			var j = 0;

			while (i < bytes.Length)
			{
				var b = bytes[i++];
				c[j++] = Alphabet[b >> 4];
				c[j++] = Alphabet[b & 0xF];
			}

			var result = new string(c);

			return result;
		}
	}
}
