namespace BilibiliLiveRecordDownLoader.Application.Recording.Containers;

// 时间从一个时间刻度换到另一个：先乘后除，乘积用 Int128 才不会溢出
internal static class TimeRescale
{
	// 四舍五入，负数按绝对值舍入
	public static long Near(long value, long multiplier, long divisor)
	{
		Int128 product = (Int128)value * multiplier;
		Int128 half = divisor / 2;
		return (long)(product >= 0 ? (product + half) / divisor : -((-product + half) / divisor));
	}

	public static long Floor(long value, long multiplier, long divisor)
	{
		(Int128 quotient, Int128 remainder) = Int128.DivRem((Int128)value * multiplier, divisor);
		return (long)(remainder < 0 ? quotient - 1 : quotient);
	}

	public static long Ceiling(long value, long multiplier, long divisor)
	{
		(Int128 quotient, Int128 remainder) = Int128.DivRem((Int128)value * multiplier, divisor);
		return (long)(remainder > 0 ? quotient + 1 : quotient);
	}
}
