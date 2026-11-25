namespace BilibiliLiveRecordDownLoader.ViewModels.Dialogs;

// 不用 ChoiceOption：它的空值念作“使用全局设置”，这里的空值是自定义，qn 另外输入
public sealed record QualityOption(StreamQuality? Quality)
{
	public static QualityOption Custom { get; } = new((StreamQuality?)null);

	public override string ToString()
	{
		return Quality is { } quality ? Labels.Of(quality) : AppLanguage.Current.QualityCustom;
	}
}
