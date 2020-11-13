using System.Windows;

namespace BilibiliLiveRecordDownLoader.Utils
{
	public static class ViewUtils
	{
		public static void ShowWindow(this Window window)
		{
			window.Show();

			if (window.WindowState == WindowState.Minimized)
			{
				window.WindowState = WindowState.Normal;
			}

			if (window.Topmost)
			{
				return;
			}

			window.Topmost = true;
			window.Topmost = false;
		}
	}
}
