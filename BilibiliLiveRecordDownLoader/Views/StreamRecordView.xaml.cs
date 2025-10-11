using BilibiliLiveRecordDownLoader.ViewModels;
using ReactiveUI;
using System.Reactive.Disposables.Fluent;

namespace BilibiliLiveRecordDownLoader.Views;

public partial class StreamRecordView
{
	public StreamRecordView(StreamRecordViewModel viewModel)
	{
		InitializeComponent();
		ViewModel = viewModel;

		this.WhenActivated(d =>
		{
			this.OneWayBind(ViewModel, vm => vm.RoomList, v => v.RoomListDataGrid.ItemsSource).DisposeWith(d);
			this.BindCommand(ViewModel, vm => vm.AddRoomCommand, v => v.AddMenuItem).DisposeWith(d);
			var selectedItem = this.WhenAnyValue(v => v.RoomListDataGrid.SelectedItem);
			var selectedItems = this.WhenAnyValue(v => v.RoomListDataGrid.SelectedItems);
			this.BindCommand(ViewModel,
				vm => vm.ModifyRoomCommand,
				v => v.ModifyMenuItem,
				selectedItem).DisposeWith(d);
			this.BindCommand(ViewModel,
				vm => vm.RemoveRoomCommand,
				v => v.RemoveMenuItem,
				selectedItems).DisposeWith(d);
			this.BindCommand(ViewModel,
				vm => vm.RefreshRoomCommand,
				v => v.RefreshMenuItem,
				selectedItems).DisposeWith(d);
			this.BindCommand(ViewModel,
				vm => vm.OpenDirCommand,
				v => v.OpenDirMenuItem,
				selectedItem).DisposeWith(d);
			this.BindCommand(ViewModel,
				vm => vm.OpenUrlCommand,
				v => v.OpenUrlMenuItem,
				selectedItems).DisposeWith(d);
		});
	}
}
