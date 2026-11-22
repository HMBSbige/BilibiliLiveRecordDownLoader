using Avalonia.Input;
using Avalonia.Interactivity;
using System.Windows.Input;

namespace BilibiliLiveRecordDownLoader.Views.Views;

public partial class RoomsView : UserControl
{
	// 移动超过这个距离才开始拖动，单击手柄只选中所在行
	private const double DragThreshold = 4;

	// 指针在列表上下边缘这个范围内时自动滚动，越靠近边缘越快
	private const double AutoScrollEdge = 40;
	private const double AutoScrollMaxStep = 20;

	private const double DropIndicatorInset = 8;

	private static readonly Cursor DragCursor = new(StandardCursorType.DragMove);

	private readonly DispatcherTimer _autoScrollTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
	private RoomListViewModel? _viewModel;
	private RoomDrag? _drag;
	private RoomItemViewModel? _focusAfterMove;

	public RoomsView()
	{
		InitializeComponent();
		_autoScrollTimer.Tick += OnAutoScroll;
		// 列表自己会把 Alt+方向键当作移动焦点，在隧道阶段先处理
		RoomList.AddHandler(KeyDownEvent, OnRoomListKeyDown, RoutingStrategies.Tunnel);
	}

	protected override void OnDataContextChanged(EventArgs e)
	{
		_viewModel?.RoomsMoved -= OnRoomsMoved;
		_viewModel = DataContext as RoomListViewModel;
		_viewModel?.RoomsMoved += OnRoomsMoved;

		base.OnDataContextChanged(e);
	}

	protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
	{
		EndDrag();
		base.OnDetachedFromVisualTree(e);
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

	private void OnRoomListKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.KeyModifiers is not KeyModifiers.Alt || e.Key is not (Key.Up or Key.Down) || _viewModel is null || _drag is not null)
		{
			return;
		}

		ICommand command = e.Key is Key.Up ? _viewModel.MoveUpCommand : _viewModel.MoveDownCommand;

		if (!command.CanExecute(null))
		{
			return;
		}

		e.Handled = true;
		_focusAfterMove = e.Source is StyledElement { DataContext: RoomItemViewModel room } ? room : null;
		command.Execute(null);
	}

	// 移动的行会重新生成，焦点随之丢失；布局更新后让焦点回到原来的行并滚动到可见。
	// 不用方向键导航的方式设置焦点，否则列表会把多选收成单选
	private void OnRoomsMoved(object? sender, IReadOnlyList<RoomItemViewModel> rooms)
	{
		if (rooms is not [var first, ..])
		{
			return;
		}

		RoomItemViewModel room = _focusAfterMove is { } focused && rooms.Contains(focused) ? focused : first;
		_focusAfterMove = null;

		Dispatcher.UIThread.Post
		(
			() =>
			{
				RoomList.ScrollIntoView(room);
				RoomList.ContainerFromItem(room)?.Focus();
			},
			DispatcherPriority.Loaded
		);
	}

	// 按住未选中的行时只拖动这一行，按住选中的行时拖动全部选中的行。
	// 拖动期间所在行保持焦点，虚拟化列表就不会回收它，指针捕获也就不会丢失
	private void OnHandlePointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (sender is not Control { DataContext: RoomItemViewModel room } handle || _viewModel is not { CanReorder: true } || _drag is not null || !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
		{
			return;
		}

		e.Handled = true;

		if (RoomList.SelectedItems?.Contains(room) is not true)
		{
			RoomList.SelectedItems?.Clear();
			RoomList.SelectedItem = room;
		}

		RoomList.ContainerFromItem(room)?.Focus();
		_drag = new RoomDrag(e.Pointer, handle, room, e.GetPosition(RoomList));
		e.Pointer.Capture(handle);
	}

	private void OnHandlePointerMoved(object? sender, PointerEventArgs e)
	{
		if (_drag is not { } drag || drag.Pointer != e.Pointer)
		{
			return;
		}

		drag.Position = e.GetPosition(RoomList);

		if (!drag.IsStarted)
		{
			if (Math.Abs(drag.Position.X - drag.Origin.X) < DragThreshold && Math.Abs(drag.Position.Y - drag.Origin.Y) < DragThreshold)
			{
				return;
			}

			StartDrag(drag);
		}

		UpdateDrop(drag);
	}

	// 在列表外松开时取消
	private void OnHandlePointerReleased(object? sender, PointerReleasedEventArgs e)
	{
		if (_drag is not { } drag || drag.Pointer != e.Pointer)
		{
			return;
		}

		e.Handled = true;
		drag.Position = e.GetPosition(RoomList);
		int? index = drag.IsStarted ? FindDrop(drag.Position)?.Index : null;
		EndDrag();

		if (index is { } target && _viewModel?.MoveSelectionToCommand is ICommand command && command.CanExecute(target))
		{
			_focusAfterMove = drag.Room;
			command.Execute(target);
		}
	}

	private void OnHandlePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
	{
		if (_drag?.Pointer == e.Pointer)
		{
			EndDrag();
		}
	}

	private void OnDragKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key is Key.Escape && _drag is not null)
		{
			e.Handled = true;
			EndDrag();
		}
	}

	private void OnDragScrollChanged(object? sender, ScrollChangedEventArgs e)
	{
		if (_drag is { IsStarted: true } drag)
		{
			UpdateDrop(drag);
		}
	}

	private void OnAutoScroll(object? sender, EventArgs e)
	{
		if (_drag is not { IsStarted: true } drag || RoomList.Scroll is not ScrollViewer viewer || !new Rect(RoomList.Bounds.Size).Contains(drag.Position))
		{
			return;
		}

		double height = RoomList.Bounds.Height;
		double y = drag.Position.Y;
		double depth = y < AutoScrollEdge ? y - AutoScrollEdge : y > height - AutoScrollEdge ? y - (height - AutoScrollEdge) : 0;
		double maxOffset = Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);

		viewer.Offset = viewer.Offset.WithY(Math.Clamp(viewer.Offset.Y + depth / AutoScrollEdge * AutoScrollMaxStep, 0, maxOffset));
	}

	private void StartDrag(RoomDrag drag)
	{
		drag.IsStarted = true;
		drag.Handle.Cursor = DragCursor;
		TopLevel.GetTopLevel(this)?.AddHandler(KeyDownEvent, OnDragKeyDown, RoutingStrategies.Tunnel);

		if (RoomList.Scroll is ScrollViewer viewer)
		{
			viewer.ScrollChanged += OnDragScrollChanged;
		}

		_autoScrollTimer.Start();
	}

	private void EndDrag()
	{
		if (_drag is not { } drag)
		{
			return;
		}

		_drag = null;
		_autoScrollTimer.Stop();
		DropIndicator.IsVisible = false;
		drag.Handle.ClearValue(CursorProperty);
		TopLevel.GetTopLevel(this)?.RemoveHandler(KeyDownEvent, OnDragKeyDown);

		if (RoomList.Scroll is ScrollViewer viewer)
		{
			viewer.ScrollChanged -= OnDragScrollChanged;
		}

		drag.Pointer.Capture(null);
	}

	private void UpdateDrop(RoomDrag drag)
	{
		if (FindDrop(drag.Position) is not { } drop || RoomList.TranslatePoint(new Point(0, drop.Y), DropLayer) is not { } point)
		{
			DropIndicator.IsVisible = false;
			return;
		}

		double thickness = DropIndicator.Height;

		DropIndicator.Width = Math.Max(0, DropLayer.Bounds.Width - DropIndicatorInset * 2);
		Canvas.SetLeft(DropIndicator, DropIndicatorInset);
		Canvas.SetTop(DropIndicator, Math.Clamp(point.Y - thickness / 2, 0, Math.Max(0, DropLayer.Bounds.Height - thickness)));
		DropIndicator.IsVisible = true;
	}

	// 插入位置和指示线在列表坐标中的位置；指针在列表外时没有放置位置
	private (int Index, double Y)? FindDrop(Point position)
	{
		if (!new Rect(RoomList.Bounds.Size).Contains(position))
		{
			return null;
		}

		(int Index, double Bottom)? last = null;

		foreach ((int index, double top, double bottom) in GetVisibleRows().OrderBy(static row => row.Index))
		{
			if (position.Y < (top + bottom) / 2)
			{
				return (index, top);
			}

			last = (index, bottom);
		}

		return last is { } row ? (row.Index + 1, row.Bottom) : null;
	}

	// 只看与可见区域相交的行，虚拟化列表为保留焦点而留下的行可能不在可见区域里
	private IEnumerable<(int Index, double Top, double Bottom)> GetVisibleRows()
	{
		double height = RoomList.Bounds.Height;

		foreach (Control container in RoomList.GetRealizedContainers())
		{
			int index = RoomList.IndexFromContainer(container);

			if (index < 0 || !container.IsVisible || container.TranslatePoint(default, RoomList) is not { } origin)
			{
				continue;
			}

			double top = origin.Y;
			double bottom = top + container.Bounds.Height;

			if (bottom > 0 && top < height)
			{
				yield return (index, top, bottom);
			}
		}
	}

	private sealed class RoomDrag(IPointer pointer, Control handle, RoomItemViewModel room, Point origin)
	{
		public IPointer Pointer { get; } = pointer;

		public Control Handle { get; } = handle;

		public RoomItemViewModel Room { get; } = room;

		public Point Origin { get; } = origin;

		public Point Position { get; set; } = origin;

		public bool IsStarted { get; set; }
	}
}
