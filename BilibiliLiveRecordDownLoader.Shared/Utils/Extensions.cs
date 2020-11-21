using Microsoft.Extensions.ObjectPool;
using System;
using System.Reactive.Disposables;
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

		public static IDisposable GetObject<T>(this ObjectPool<T> objectPool, out T res) where T : class
		{
			var b = objectPool.Get();
			res = b;
			return Disposable.Create(() => objectPool.Return(b));
		}
	}
}
