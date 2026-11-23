namespace BilibiliLiveRecordDownLoader.ViewModels.Dialogs;

public sealed partial class ConfirmDialogViewModel : DialogViewModel<bool>
{
	public ConfirmDialogViewModel(string title, string message, string confirmText, bool isDestructive)
	{
		Title = title;
		Message = message;
		ConfirmText = confirmText;
		IsDestructive = isDestructive;
		ConfirmCommand.DisposeWith(Disposables);
	}

	public override string Title { get; }

	public string Message { get; }

	public string ConfirmText { get; }

	public bool IsDestructive { get; }

	[ReactiveCommand]
	private void Confirm()
	{
		Close(true);
	}
}
