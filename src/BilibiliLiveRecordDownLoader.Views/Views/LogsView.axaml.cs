using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using System.Collections.Specialized;

namespace BilibiliLiveRecordDownLoader.Views.Views;

public partial class LogsView : UserControl
{
	private const double EdgeTolerance = 4;
	private const double MinColumnWidth = 40;

	// NaN 表示按内容自适应
	public static readonly StyledProperty<double> TimeColumnWidthProperty = AvaloniaProperty.Register<LogsView, double>(nameof(TimeColumnWidth), double.NaN);

	public static readonly StyledProperty<double> LevelColumnWidthProperty = AvaloniaProperty.Register<LogsView, double>(nameof(LevelColumnWidth), double.NaN);

	public static readonly StyledProperty<double> RoomColumnWidthProperty = AvaloniaProperty.Register<LogsView, double>(nameof(RoomColumnWidth), double.NaN);

	private LogViewModel? _viewModel;
	private bool _isScrollPending;

	public LogsView()
	{
		InitializeComponent();
	}

	public double TimeColumnWidth
	{
		get => GetValue(TimeColumnWidthProperty);
		set => SetValue(TimeColumnWidthProperty, value);
	}

	public double LevelColumnWidth
	{
		get => GetValue(LevelColumnWidthProperty);
		set => SetValue(LevelColumnWidthProperty, value);
	}

	public double RoomColumnWidth
	{
		get => GetValue(RoomColumnWidthProperty);
		set => SetValue(RoomColumnWidthProperty, value);
	}

	protected override void OnDataContextChanged(EventArgs e)
	{
		_viewModel?.Entries.CollectionChanged -= OnEntriesChanged;
		_viewModel = DataContext as LogViewModel;
		_viewModel?.Entries.CollectionChanged += OnEntriesChanged;

		base.OnDataContextChanged(e);
	}

	private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.Action is not (NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Reset) || _isScrollPending || _viewModel is not { Sort.Column: LogSortColumn.Time } || !IsAtNewestEdge())
		{
			return;
		}

		_isScrollPending = true;
		Dispatcher.UIThread.Post(ScrollToNewest, DispatcherPriority.Background);
	}

	private void ScrollToNewest()
	{
		_isScrollPending = false;

		if (_viewModel is { Entries: [{ } first, ..] entries } viewModel)
		{
			LogList.ScrollIntoView(viewModel.Sort.IsDescending ? first : entries[^1]);
		}
	}

	private bool IsAtNewestEdge()
	{
		if (LogList.Scroll is not { } scroll)
		{
			return true;
		}

		return _viewModel is { Sort.IsDescending: true }
			? scroll.Offset.Y <= EdgeTolerance
			: scroll.Offset.Y + scroll.Viewport.Height >= scroll.Extent.Height - EdgeTolerance;
	}

	private void OnTimeColumnResize(object? sender, VectorEventArgs e)
	{
		ResizeColumn(sender, e, TimeColumnWidthProperty);
	}

	private void OnLevelColumnResize(object? sender, VectorEventArgs e)
	{
		ResizeColumn(sender, e, LevelColumnWidthProperty);
	}

	private void OnRoomColumnResize(object? sender, VectorEventArgs e)
	{
		ResizeColumn(sender, e, RoomColumnWidthProperty);
	}

	private void ResizeColumn(object? sender, VectorEventArgs e, StyledProperty<double> property)
	{
		if (sender is Thumb { Parent: Control cell })
		{
			SetValue(property, Math.Max(MinColumnWidth, cell.Bounds.Width + e.Vector.X));
		}
	}

	private async void OnCopyClick(object? sender, RoutedEventArgs e)
	{
		await CopySelectionAsync();
	}

	private async void OnLogListKeyDown(object? sender, KeyEventArgs e)
	{
		if (Avalonia.Application.Current?.PlatformSettings?.HotkeyConfiguration.Copy is { } gestures && gestures.Any(gesture => gesture.Matches(e)))
		{
			e.Handled = true;
			await CopySelectionAsync();
		}
	}

	// 剪贴板可能被其他程序占用
	private async Task CopySelectionAsync()
	{
		if (_viewModel is null || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard || LogList.SelectedItems is not { Count: > 0 } selection)
		{
			return;
		}

		IEnumerable<string> lines = _viewModel.Entries
			.Where(selection.Contains)
			.Select(static entry => string.Create(CultureInfo.CurrentCulture, $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{entry.Level}] [{entry.RoomId}] {entry.Message}"));

		try
		{
			await clipboard.SetTextAsync(string.Join(Environment.NewLine, lines));
		}
		catch (Exception exception)
		{
			RxState.DefaultExceptionHandler.OnNext(exception);
		}
	}
}
