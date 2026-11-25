using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;

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

	// 房间编辑器里的选项；接口里没见过 401，不列出
	public static IReadOnlyList<StreamQuality> Selectable { get; } = [Dolby, OriginalTrueColor, UltraHd4K, Qhd2K, Original, BluRay, SuperHigh, High, Smooth, Default];

	// 接口没给名称时用；同一 qn 在不同直播间可能叫法不同，注释里是见过的其他名称
	public string? KnownName => Qn switch
	{
		0 => AppLanguage.Current.QualityDefault,
		80 => AppLanguage.Current.QualitySmooth,
		150 => AppLanguage.Current.QualityHigh,
		// 720P 超清
		250 => AppLanguage.Current.QualitySuperHigh,
		// 1080P 蓝光
		400 => AppLanguage.Current.QualityBluRay,
		401 => AppLanguage.Current.QualityBluRayDolby,
		// 1080P 高码率、1080P 高码率 高帧率
		10000 => AppLanguage.Current.QualityOriginal,
		15000 => AppLanguage.Current.QualityQhd2K,
		20000 => AppLanguage.Current.QualityUltraHd4K,
		// 2K 原画、2K 原画 高帧率
		25000 => AppLanguage.Current.QualityOriginalTrueColor,
		30000 => AppLanguage.Current.QualityDolby,
		_ => null
	};
}
