namespace BilibiliLiveRecordDownLoader.Views.Services;

internal sealed class ThemeService : IThemeService
{
	public void Apply(AppTheme theme)
	{
		if (Avalonia.Application.Current is not { } application)
		{
			return;
		}

		application.RequestedThemeVariant = theme switch
		{
			AppTheme.Light => ThemeVariant.Light,
			AppTheme.Dark => ThemeVariant.Dark,
			AppTheme.FollowSystem => ThemeVariant.Default
		};
	}
}
