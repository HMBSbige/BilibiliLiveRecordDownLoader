using BilibiliLiveRecordDownLoader.ViewModels;
using ReactiveUI;
using System.Reactive.Disposables;

namespace BilibiliLiveRecordDownLoader.Views;

public partial class LogView
{
	public LogView(LogViewModel viewModel)
	{
		InitializeComponent();
		ViewModel = viewModel;

		this.WhenActivated(d =>
		{
			this.OneWayBind(ViewModel, vm => vm.Logs, v => v.LogDataGrid.ItemsSource).DisposeWith(d);
		});
	}
}
