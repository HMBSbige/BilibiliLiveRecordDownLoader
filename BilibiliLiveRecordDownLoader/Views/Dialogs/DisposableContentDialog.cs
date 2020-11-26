using ModernWpf.Controls;
using System;

namespace BilibiliLiveRecordDownLoader.Views.Dialogs
{
	public class DisposableContentDialog : ContentDialog, IDisposable
	{
		public virtual void Dispose()
		{
			Hide();
		}
	}
}
