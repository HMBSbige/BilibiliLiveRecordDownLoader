namespace BilibiliLiveRecordDownLoader.ViewModels.Dialogs;

public abstract partial class DialogViewModel : ViewModelBase
{
	protected DialogViewModel()
	{
		CanDismiss = this.WhenChanged(static viewModel => viewModel.IsBusy).Map(static isBusy => !isBusy);
	}

	public abstract string Title { get; }

	// 提交进行中不能关闭，否则提交仍会生效且失败原因无处显示
	[Reactive]
	public partial bool IsBusy { get; protected set; }

	public IObservable<bool> CanDismiss { get; }

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

	[ReactiveCommand(CanExecute = nameof(CanDismiss))]
	private void DismissDialog()
	{
		Dismiss();
	}
}
