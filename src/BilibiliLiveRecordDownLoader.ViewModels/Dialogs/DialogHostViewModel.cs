using System.Diagnostics.CodeAnalysis;

namespace BilibiliLiveRecordDownLoader.ViewModels.Dialogs;

public sealed partial class DialogHostViewModel : ViewModelBase
{
	[SuppressMessage("Usage", "CA2213", Justification = "没有需要释放的等待句柄，释放时关掉的对话框之后还要在 finally 里 Release")]
	private readonly SemaphoreSlim _gate = new(1, 1);

	[Reactive]
	public partial DialogViewModel? Current { get; private set; }

	public async Task<TResult?> ShowAsync<TResult>(DialogViewModel<TResult> dialog)
	{
		await _gate.WaitAsync();

		try
		{
			Current = dialog;
			dialog.OnOpened();
			return await dialog.Result;
		}
		finally
		{
			Current = null;
			dialog.Dispose();
			_gate.Release();
		}
	}

	public Task<bool> ConfirmAsync(string title, string message, string confirmText, bool isDestructive)
	{
		return ShowAsync(new ConfirmDialogViewModel(title, message, confirmText, isDestructive));
	}

	public override void Dispose()
	{
		Current?.Dismiss();
		base.Dispose();
	}
}
