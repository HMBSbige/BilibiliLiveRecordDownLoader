using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace BilibiliLiveRecordDownLoader.Shared.Utils
{
	public static class Extensions
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void NoWarning(this Task _) { }

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void NoWarning(this ValueTask _) { }

		public static int GetDeterministicHashCode(this string str)
		{
			unchecked
			{
				var hash1 = (5381 << 16) + 5381;
				var hash2 = hash1;

				for (var i = 0; i < str.Length; i += 2)
				{
					hash1 = ((hash1 << 5) + hash1) ^ str[i];
					if (i == str.Length - 1)
					{
						break;
					}

					hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
				}

				return hash1 + hash2 * 1566083941;
			}
		}

		public static string ToCookie(this IEnumerable<string> cookies)
		{
			var hashSet = new HashSet<string>();
			foreach (var cookie in cookies)
			{
				var keyValue = cookie.Split(';', StringSplitOptions.TrimEntries).FirstOrDefault();
				if (keyValue is not null and not @"")
				{
					hashSet.Add(keyValue);
				}
			}
			return string.Join(';', hashSet);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static IEnumerable<KeyValuePair<string?, string?>> Cast(this Dictionary<string, string> pair)
		{
			//TODO: .NET 6.0
			// ReSharper disable once RedundantCast
			return (IEnumerable<KeyValuePair<string?, string?>>)pair;
		}
	}
}
