namespace BilibiliLiveRecordDownLoader.ViewModels.Settings;

public sealed record LanguageOptionViewModel(string CultureName, string DisplayName)
{
	public override string ToString()
	{
		return string.IsNullOrEmpty(CultureName) ? AppLanguage.Current.FollowSystem : DisplayName;
	}
}
