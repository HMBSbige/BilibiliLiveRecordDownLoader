using System.Runtime.CompilerServices;

namespace BilibiliLiveRecordDownLoader.FlvProcessor.Utils;

internal static class BitOperations
{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int Read(ref ulong x, int offset)
	{
		var r = (int)(x >> (64 - offset));
		x <<= offset;
		return r;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Write(ref ulong x, int offset, int value)
	{
		var mask = ulong.MaxValue >> (64 - offset);
		x = (x << offset) | ((ulong)value & mask);
	}
}
