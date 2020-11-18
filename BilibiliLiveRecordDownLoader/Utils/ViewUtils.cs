using System.Windows;

namespace BilibiliLiveRecordDownLoader.Utils
{
	public static class ViewUtils
	{
		public static void ShowWindow(this Window window)
		{
			if (!IsOnScreen(window))
			{
				window.ToCenter();
			}

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

		public static bool IsOnScreen(double x, double y)
		{
			return
				SystemParameters.VirtualScreenLeft <= x &&
				SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth >= x &&
				SystemParameters.VirtualScreenTop <= y &&
				SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight >= y;
		}

		public static bool IsOnScreen(double left, double top, double width, double height)
		{
			return IsOnScreen(left, top) || IsOnScreen(left + width, top + height);
		}

		public static bool IsOnScreen(Window window)
		{
			return IsOnScreen(window.Left, window.Top, window.Width, window.Height);
		}

		public static void ToCenter(this Window window)
		{
			var windowWidth = window.Width;
			var windowHeight = window.Height;
			window.Left = SystemParameters.PrimaryScreenWidth / 2 - windowWidth / 2;
			window.Top = SystemParameters.PrimaryScreenHeight / 2 - windowHeight / 2;
		}
	}
}
