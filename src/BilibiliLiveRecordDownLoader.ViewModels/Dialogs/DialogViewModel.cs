namespace BilibiliLiveRecordDownLoader.ViewModels.Dialogs;

public abstract class DialogViewModel : ViewModelBase
{
	public abstract string Title { get; }

	public virtual void OnOpened()
	{
	}

	internal abstract Task Completion { get; }

	public abstract void Dismiss();
}

public abstract partial class DialogViewModel<TResult> : DialogViewModel
{
	private readonly TaskCompletionSource<TResult?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

	protected DialogViewModel()
	{
		DismissDialogCommand.DisposeWith(Disposables);
	}

	internal Task<TResult?> Result => _result.Task;

	internal override Task Completion => _result.Task;

	public override void Dismiss()
	{
		_result.TrySetResult(default);
	}

	protected void Close(TResult result)
	{
		_result.TrySetResult(result);
	}

	[ReactiveCommand]
	private void DismissDialog()
	{
		Dismiss();
	}
}
