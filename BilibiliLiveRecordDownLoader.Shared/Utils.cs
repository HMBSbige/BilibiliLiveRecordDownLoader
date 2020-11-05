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
    }
}
