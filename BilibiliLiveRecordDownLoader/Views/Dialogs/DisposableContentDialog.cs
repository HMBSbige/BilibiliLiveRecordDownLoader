using ModernWpf.Controls;
using System;

namespace BilibiliLiveRecordDownLoader.Views.Dialogs
{
	public class DisposableContentDialog : ContentDialog, IDisposable
	{
		public void Dispose()
		{
			Hide();
		}
	}
}
