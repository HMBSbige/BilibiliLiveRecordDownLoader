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

	private LogViewModel? _viewModel;
	private bool _isScrollPending;

	public LogsView()
	{
		InitializeComponent();
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

	// 表头单元格的宽度一开始是 NaN，按内容自适应；各行的单元格绑定它
	private void OnColumnResize(object? sender, VectorEventArgs e)
	{
		if (sender is Thumb { Parent: Control cell })
		{
			cell.Width = Math.Max(MinColumnWidth, cell.Bounds.Width + e.Vector.X);
		}
	}

	private async void OnCopyClick(object? sender, RoutedEventArgs e)
	{
		await CopySelectionAsync();
	}

	private async void OnLogListKeyDown(object? sender, KeyEventArgs e)
	{
		if (Avalonia.Application.Current?.PlatformSettings?.HotkeyConfiguration.Copy is { } gestures && gestures.Exists(gesture => gesture.Matches(e)))
		{
			e.Handled = true;
			await CopySelectionAsync();
		}
	}

	// 剪贴板可能被其他程序占用
	private async Task CopySelectionAsync()
	{
		if (_viewModel is not { Entries: var entries } || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard || LogList.Selection.SelectedIndexes is not { Count: > 0 } indexes)
		{
			return;
		}

		// 选中的索引按升序排列，正是显示顺序
		IEnumerable<string> lines = indexes
			.Select(index => entries[index])
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
