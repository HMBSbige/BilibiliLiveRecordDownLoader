namespace BilibiliLiveRecordDownLoader.ViewModels.Dialogs;

public sealed partial class DialogHostViewModel : ViewModelBase
{
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

	public async Task<bool> ConfirmAsync(string title, string message)
	{
		return await ShowAsync(new ConfirmDialogViewModel(title, message));
	}

	public override void Dispose()
	{
		Current?.Dismiss();
		_gate.Dispose();
		base.Dispose();
	}
}
