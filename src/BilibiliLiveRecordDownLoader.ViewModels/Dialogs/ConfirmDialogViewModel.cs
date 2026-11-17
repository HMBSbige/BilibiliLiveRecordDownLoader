namespace BilibiliLiveRecordDownLoader.ViewModels.Dialogs;

public sealed partial class ConfirmDialogViewModel : DialogViewModel<bool>
{
	public ConfirmDialogViewModel(string title, string message)
	{
		Title = title;
		Message = message;
		ConfirmCommand.DisposeWith(Disposables);
	}

	public override string Title { get; }

	public string Message { get; }

	[ReactiveCommand]
	private void Confirm()
	{
		Close(true);
	}
}
