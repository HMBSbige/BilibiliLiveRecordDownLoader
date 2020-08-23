using System.Windows;

namespace BilibiliLiveRecordDownLoader.Utils
{
    public static class ViewUtils
    {
        public static void ShowWindow(this Window window)
        {
            window.Visibility = Visibility.Visible;

            Win32.UnMinimize(window);

            if (window.Topmost) return;

            window.Topmost = true;
            window.Topmost = false;
        }
    }
}
