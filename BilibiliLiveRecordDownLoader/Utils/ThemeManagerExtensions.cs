using BilibiliLiveRecordDownLoader.Enums;
using ModernWpf;
using ReactiveUI;
using System.Reactive.Concurrency;

namespace BilibiliLiveRecordDownLoader.Utils
{
	public static class ThemeManagerExtensions
	{
		public static void SetTheme(this ThemeManager manager, Theme theme)
		{
			switch (theme)
			{
				case Theme.亮:
				{
					manager.SetTheme(ApplicationTheme.Light);
					break;
				}
				case Theme.暗:
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
