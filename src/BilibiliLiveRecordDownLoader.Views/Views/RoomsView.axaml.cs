using Avalonia.Input;
using System.Windows.Input;

namespace BilibiliLiveRecordDownLoader.Views.Views;

public partial class RoomsView : UserControl
{
	public RoomsView()
	{
		InitializeComponent();
	}

	private void OnRoomDoubleTapped(object? sender, TappedEventArgs e)
	{
		if (DataContext is RoomListViewModel { EditRoomCommand: ICommand command } && RoomList.SelectedItem is RoomItemViewModel room && command.CanExecute(room))
		{
			command.Execute(room);
		}
	}
}
