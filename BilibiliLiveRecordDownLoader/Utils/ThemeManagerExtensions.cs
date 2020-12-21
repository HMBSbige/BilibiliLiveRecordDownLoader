using ModernWpf;
using ReactiveUI;
using System.Reactive.Concurrency;

namespace BilibiliLiveRecordDownLoader.Utils
{
	public static class ThemeManagerExtensions
	{
		public static void SetTheme(this ThemeManager manager, ElementTheme theme)
		{
			switch (theme)
			{
				case ElementTheme.Light:
				{
					manager.SetTheme(ApplicationTheme.Light);
					break;
				}
				case ElementTheme.Dark:
				{
					manager.SetTheme(ApplicationTheme.Dark);
					break;
				}
				default:
				{
					manager.SetTheme(null);
					break;
				}
			}
		}

		private static void SetTheme(this ThemeManager manager, ApplicationTheme? theme)
		{
			RxApp.MainThreadScheduler.Schedule(() => manager.ApplicationTheme = theme);
		}
	}
}
