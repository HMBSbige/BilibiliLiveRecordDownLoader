using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System.ComponentModel;
using System.Windows.Input;

namespace BilibiliLiveRecordDownLoader.Views.Views;

public partial class RoomsView : UserControl
{
	// 单击手柄只选中所在行
	private const double DragThreshold = 4;

	private const double AutoScrollEdge = 40;
	private const double AutoScrollMaxStep = 20;

	private const double DropIndicatorInset = 8;

	private const double DanmakuPanelMinWidth = 300;

	private static readonly Cursor DragCursor = new(StandardCursorType.DragMove);

	private readonly DispatcherTimer _autoScrollTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
	private RoomListViewModel? _viewModel;
	private RoomDrag? _drag;
	private RoomItemViewModel? _focusAfterMove;

	private GridLength _danmakuPanelWidth = new(360);

	public RoomsView()
	{
		InitializeComponent();
		_autoScrollTimer.Tick += OnAutoScroll;
		ContentGrid.SizeChanged += OnContentGridSizeChanged;
		// 列表自己会把 Alt+方向键当作移动焦点，在隧道阶段先处理
		RoomList.AddHandler(KeyDownEvent, OnRoomListKeyDown, RoutingStrategies.Tunnel);
	}

	protected override void OnDataContextChanged(EventArgs e)
	{
		_viewModel?.RoomsMoved -= OnRoomsMoved;
		_viewModel?.Danmaku.PropertyChanged -= OnDanmakuPropertyChanged;
		_viewModel = DataContext as RoomListViewModel;
		_viewModel?.RoomsMoved += OnRoomsMoved;
		_viewModel?.Danmaku.PropertyChanged += OnDanmakuPropertyChanged;
		UpdateDanmakuPanel();

		base.OnDataContextChanged(e);
	}

	protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
	{
		EndDrag();
		base.OnDetachedFromVisualTree(e);
	}

	private void OnDanmakuPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(DanmakuPanelViewModel.IsOpen))
		{
			UpdateDanmakuPanel();
		}
	}

	private void UpdateDanmakuPanel()
	{
		bool isOpen = _viewModel?.Danmaku.IsOpen is true;
		ColumnDefinition column = ContentGrid.ColumnDefinitions[2];

		if (!isOpen && column.Width.Value > 0)
		{
			_danmakuPanelWidth = column.Width;
		}

		column.MinWidth = isOpen ? DanmakuPanelMinWidth : 0;
		column.Width = isOpen ? _danmakuPanelWidth : new GridLength(0);
		DanmakuSplitter.IsVisible = isOpen;
		DanmakuPanel.IsVisible = isOpen;
		UpdateDanmakuPanelMaxWidth();
	}

	private void OnContentGridSizeChanged(object? sender, SizeChangedEventArgs e)
	{
		UpdateDanmakuPanelMaxWidth();
	}

	// 像素宽度的列不随窗口变窄；只限 MaxWidth 不改 Width，窗口放大后能恢复
	private void UpdateDanmakuPanelMaxWidth()
	{
		ColumnDefinition column = ContentGrid.ColumnDefinitions[2];

		column.MaxWidth = _viewModel?.Danmaku.IsOpen is true
			? Math.Max(DanmakuPanelMinWidth, ContentGrid.Bounds.Width - ContentGrid.ColumnDefinitions[0].MinWidth - DanmakuSplitter.Width)
			: double.PositiveInfinity;
	}

	// 双击手势按连击数另外触发，按钮处理了按下也拦不住
	private void OnRoomDoubleTapped(object? sender, TappedEventArgs e)
	{
		if (IsOnRow(e.Source) && DataContext is RoomListViewModel { EditRoomCommand: ICommand command } && command.CanExecute(null))
		{
			command.Execute(null);
		}
	}

	private static bool IsOnRow(object? source)
	{
		if (source is not Visual visual)
		{
			return false;
		}

		foreach (Visual element in visual.GetSelfAndVisualAncestors())
		{
			if (element is Button || element.Classes.Contains("dragHandle"))
			{
				return false;
			}

			if (element is ListBoxItem)
			{
				return true;
			}
		}

		return false;
	}

	// 更多菜单就是右键菜单，只能在挂载它的列表上打开，所以临时改为贴着按钮弹出
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

	// 移动的行会重新生成而丢失焦点；不用方向键导航的方式设焦点，否则列表会把多选收成单选
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

	// 虚拟化列表为保留焦点而留下的行可能不在可见区域里
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
