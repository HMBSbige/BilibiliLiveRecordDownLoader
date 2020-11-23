using BilibiliLiveRecordDownLoader.ViewModels;
using ReactiveUI;
using System.Reactive.Disposables;

namespace BilibiliLiveRecordDownLoader.Views
{
	public partial class StreamRecordView
	{
		public StreamRecordView(StreamRecordViewModel viewModel)
		{
			InitializeComponent();
			ViewModel = viewModel;

			this.WhenActivated(d =>
			{
				this.OneWayBind(ViewModel, vm => vm.RoomList, v => v.RoomListDataGrid.ItemsSource).DisposeWith(d);
				this.WhenAnyValue(v => v.RoomListDataGrid.SelectedItem)
						.BindTo(ViewModel, vm => vm.SelectedItem)
						.DisposeWith(d);
				this.WhenAnyValue(v => v.RoomListDataGrid.SelectedItems)
						.BindTo(ViewModel, vm => vm.SelectedItems)
						.DisposeWith(d);
				this.BindCommand(ViewModel, vm => vm.AddRoomCommand, v => v.AddMenuItem).DisposeWith(d);
				this.BindCommand(ViewModel, vm => vm.ModifyRoomCommand, v => v.ModifyMenuItem, vm => vm.SelectedItem).DisposeWith(d);
				this.BindCommand(ViewModel, vm => vm.RemoveRoomCommand, v => v.RemoveMenuItem, vm => vm.SelectedItems).DisposeWith(d);
				this.BindCommand(ViewModel, vm => vm.RefreshRoomCommand, v => v.RefreshMenuItem, vm => vm.SelectedItems).DisposeWith(d);
				this.BindCommand(ViewModel, vm => vm.OpenDirCommand, v => v.OpenDirMenuItem, vm => vm.SelectedItem).DisposeWith(d);
				this.BindCommand(ViewModel, vm => vm.OpenUrlCommand, v => v.OpenUrlMenuItem, vm => vm.SelectedItems).DisposeWith(d);
			});
		}
	}
}
