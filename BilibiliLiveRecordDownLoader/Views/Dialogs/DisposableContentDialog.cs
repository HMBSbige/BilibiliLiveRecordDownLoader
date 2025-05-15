using BilibiliLiveRecordDownLoader.Services;
using BilibiliLiveRecordDownLoader.Utils;
using ModernWpf.Controls;
using Punchclock;

namespace BilibiliLiveRecordDownLoader.Views.Dialogs;

public class DisposableContentDialog : ContentDialog, IDisposable
{
	private readonly OperationQueue _queue;

	public DisposableContentDialog()
	{
		_queue = DI.GetRequiredService<OperationQueue>();
		Owner = DI.GetRequiredService<MainWindow>();
	}

	public virtual void Dispose()
	{
		Hide();
		GC.SuppressFinalize(this);
	}

	public async Task<ContentDialogResult> SafeShowAsync(int priority = 1, ContentDialogResult defaultResult = ContentDialogResult.None)
	{
		return await _queue.Enqueue(priority, TaskQueueKeyConstants.ContentDialogKey, async () =>
		{
			ContentDialogResult res = defaultResult;
			try
			{
#pragma warning disable VSTHRD001
				await Dispatcher.Invoke(async () =>
#pragma warning restore VSTHRD001
				{
					Owner.Focus();
					res = await ShowAsync();
				});
			}
			catch (InvalidOperationException)
			{

			}
			return res;
		});
	}
}
