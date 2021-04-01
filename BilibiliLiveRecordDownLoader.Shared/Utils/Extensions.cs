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
