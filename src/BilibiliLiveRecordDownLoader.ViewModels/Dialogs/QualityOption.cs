namespace BilibiliLiveRecordDownLoader.ViewModels.Dialogs;

// 不用 ChoiceOption：它的空值念作“使用全局设置”，这里的空值是自定义，qn 另外输入
public sealed record QualityOption(StreamQuality? Quality)
{
	public static QualityOption Custom { get; } = new((StreamQuality?)null);

	// 从高到低，最后是哔哩哔哩默认和自定义
	public static IReadOnlyList<QualityOption> All { get; } =
	[
		new(StreamQuality.Dolby),
		new(StreamQuality.OriginalTrueColor),
		new(StreamQuality.UltraHd4K),
		new(StreamQuality.Qhd2K),
		new(StreamQuality.Original),
		new(StreamQuality.BluRayDolby),
		new(StreamQuality.BluRay),
		new(StreamQuality.SuperHigh),
		new(StreamQuality.High),
		new(StreamQuality.Smooth),
		new(StreamQuality.Default),
		Custom
	];

	public override string ToString()
	{
		return Quality is { } quality ? StreamLabels.Of(quality) : AppLanguage.Current.QualityCustom;
	}
}
