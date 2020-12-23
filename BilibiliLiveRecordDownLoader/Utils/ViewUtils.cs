using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

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

			window.Focus();
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
			if (window.Width > SystemParameters.PrimaryScreenWidth)
			{
				window.Width = 1280;
			}

			if (window.Height > SystemParameters.PrimaryScreenHeight)
			{
				window.Height = 720;
			}

			window.Left = SystemParameters.PrimaryScreenWidth / 2 - window.Width / 2;
			window.Top = SystemParameters.PrimaryScreenHeight / 2 - window.Height / 2;
		}

		public static string? GetDropPath(DragEventArgs e)
		{
			var data = e.Data.GetData(DataFormats.FileDrop);
			if (data is string?[] { Length: > 0 } array)
			{
				return array.FirstOrDefault(File.Exists);
			}

			return null;
		}

		public static IDisposable DropPathEvent(this TextBox textBox)
		{
			return textBox.Events().PreviewDrop.Subscribe(e =>
			{
				var s = GetDropPath(e);
				if (s is null)
				{
					return;
				}
				textBox.Text = s;
				e.Handled = true;
			});
		}

		public static IDisposable ShowDragOverIconEvent(this FrameworkElement control)
		{
			return control.Events().PreviewDragOver.Subscribe(e => e.Handled = true);
		}
	}
}
