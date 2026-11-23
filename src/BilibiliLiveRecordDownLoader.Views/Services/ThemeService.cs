namespace BilibiliLiveRecordDownLoader.Views.Services;

internal sealed class ThemeService : IThemeService
{
	public void Apply(AppTheme theme)
	{
		Avalonia.Application.Current?.RequestedThemeVariant = theme switch
		{
			AppTheme.Light => ThemeVariant.Light,
			AppTheme.Dark => ThemeVariant.Dark,
			AppTheme.FollowSystem => ThemeVariant.Default
		};
	}
}
