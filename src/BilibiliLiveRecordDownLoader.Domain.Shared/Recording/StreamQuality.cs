namespace BilibiliLiveRecordDownLoader.Domain.Shared.Recording;

// 哔哩哔哩的画质代码 qn：各直播间支持的画质不同，以后还可能新增，所以不限于下面已知的几种；除 0 以外 qn 越大画质越高
public readonly record struct StreamQuality(long Qn)
{
	// 由哔哩哔哩决定给哪种画质
	public static StreamQuality Default { get; } = new(0);

	public static StreamQuality Smooth { get; } = new(80);

	public static StreamQuality High { get; } = new(150);

	public static StreamQuality SuperHigh { get; } = new(250);

	public static StreamQuality BluRay { get; } = new(400);

	public static StreamQuality BluRayDolby { get; } = new(401);

	public static StreamQuality Original { get; } = new(10000);

	public static StreamQuality Qhd2K { get; } = new(15000);

	public static StreamQuality UltraHd4K { get; } = new(20000);

	// 未经哔哩哔哩转码的原始流
	public static StreamQuality OriginalTrueColor { get; } = new(25000);

	public static StreamQuality Dolby { get; } = new(30000);

	// 负数拿去请求时选流会退到最低画质
	public bool IsValid => Qn >= 0;

	public static bool TryParse(ReadOnlySpan<char> text, out StreamQuality quality)
	{
		// NumberStyles.None 不接受正负号，解析出来的都是合法的 qn
		bool isParsed = long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long qn);
		quality = new StreamQuality(qn);
		return isParsed;
	}
}
