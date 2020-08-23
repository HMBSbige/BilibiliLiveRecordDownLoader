using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BilibiliLiveRecordDownLoader.Utils
{
    public static class Win32
    {
        #region UnMinimize

        public static void UnMinimize(Window window)
        {
            if (PresentationSource.FromVisual(window) is HwndSource hs)
            {
                var handle = hs.Handle;
                ShowWindow(handle, ShowWindowCommands.Restore);
            }
        }

        [DllImport(@"user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, ShowWindowCommands nCmdShow);

        //http://pinvoke.net/default.aspx/Enums/ShowWindowCommand.html
        private enum ShowWindowCommands
        {
            /// <summary>
            /// Activates and displays the window. If the window is minimized or 
            /// maximized, the system restores it to its original size and position. 
            /// An application should specify this flag when restoring a minimized window.
            /// </summary>
            Restore = 9,
        }

        #endregion
    }
}
