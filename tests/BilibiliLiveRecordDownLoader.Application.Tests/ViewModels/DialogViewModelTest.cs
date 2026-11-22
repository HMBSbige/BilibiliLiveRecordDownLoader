using BilibiliLiveRecordDownLoader.ViewModels.Dialogs;
using System.Windows.Input;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class DialogViewModelTest
{
	// 宿主只知道非泛型的对话框
	[Test]
	public async Task DialogCannotBeDismissedWhileBusyAsync()
	{
		using TestDialog dialog = new();
		DialogViewModel hosted = dialog;
		ICommand dismiss = hosted.DismissDialogCommand;

		dialog.SetBusy(true);
		bool whileBusy = dismiss.CanExecute(null);
		dialog.SetBusy(false);
		bool afterwards = dismiss.CanExecute(null);
		dismiss.Execute(null);

		using (Assert.Multiple())
		{
			await Assert.That(whileBusy).IsFalse();
			await Assert.That(afterwards).IsTrue();
			await Assert.That(dialog.Result.IsCompleted).IsTrue();
		}
	}

	private sealed class TestDialog : DialogViewModel<bool>
	{
		public override string Title => "test";

		public void SetBusy(bool isBusy)
		{
			IsBusy = isBusy;
		}
	}
}
