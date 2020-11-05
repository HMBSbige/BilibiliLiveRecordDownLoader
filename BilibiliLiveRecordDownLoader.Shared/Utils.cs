using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace BilibiliLiveRecordDownLoader.Shared
{
    public static class Utils
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void NoWarning(this Task _) { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void NoWarning(this ValueTask _) { }

        public static IDisposable CreateArray<T>(int minimumLength, out T[] res)
        {
            return ArrayPool<T>.Shared.CreateArray(minimumLength, out res);
        }
    }
}
