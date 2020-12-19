using BilibiliLiveRecordDownLoader.Services;
using ModernWpf.Controls;
using System;
using System.Threading.Tasks;

namespace BilibiliLiveRecordDownLoader.Views.Dialogs
{
	public class DisposableContentDialog : ContentDialog, IDisposable
	{
		public virtual void Dispose()
		{
			Hide();
		}

		public new Task<ContentDialogResult> ShowAsync()
		{
			Hide();
			if (Owner is not null)
			{
				Owner.Focus();
			}
			else
			{
				DI.GetService<MainWindow>().Focus();
			}
			return base.ShowAsync();
		}
	}
}
