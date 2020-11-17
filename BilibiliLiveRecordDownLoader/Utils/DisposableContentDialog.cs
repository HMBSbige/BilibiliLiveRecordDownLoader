using ModernWpf.Controls;
using System;

namespace BilibiliLiveRecordDownLoader.Utils
{
	public sealed class DisposableContentDialog : ContentDialog, IDisposable
	{
		public void Dispose()
		{
			Hide();
		}
	}
}
