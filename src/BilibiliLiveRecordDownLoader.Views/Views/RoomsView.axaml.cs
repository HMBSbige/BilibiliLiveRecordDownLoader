using Avalonia.Input;
using Avalonia.Interactivity;
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
		if (DataContext is RoomListViewModel { EditRoomCommand: ICommand command } && command.CanExecute(null))
		{
			command.Execute(null);
		}
	}

	// 更多菜单就是右键菜单，先选中所在行；菜单只能在挂载它的列表上打开，所以临时改为贴着按钮弹出
	private void OnMoreClick(object? sender, RoutedEventArgs e)
	{
		if (sender is not Control { DataContext: RoomItemViewModel room } button || RoomList.ContextMenu is not { } menu)
		{
			return;
		}

		RoomList.SelectedItems?.Clear();
		RoomList.SelectedItem = room;
		menu.PlacementTarget = button;
		menu.Placement = PlacementMode.BottomEdgeAlignedRight;

		try
		{
			menu.Open(RoomList);
		}
		finally
		{
			menu.ClearValue(ContextMenu.PlacementTargetProperty);
			menu.ClearValue(ContextMenu.PlacementProperty);
		}
	}
}
