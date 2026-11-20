using System.Collections.Specialized;

namespace BilibiliLiveRecordDownLoader.Views.Views;

public partial class LogsView : UserControl
{
	private const double BottomTolerance = 4;

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

	// 只有停在底部时才跟随新日志，一批新增只滚动一次
	private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.Action is not NotifyCollectionChangedAction.Add || _isScrollPending || !IsAtBottom())
		{
			return;
		}

		_isScrollPending = true;
		Dispatcher.UIThread.Post(ScrollToLast, DispatcherPriority.Background);
	}

	private void ScrollToLast()
	{
		_isScrollPending = false;

		if (_viewModel?.Entries is [.., { } last])
		{
			LogList.ScrollIntoView(last);
		}
	}

	private bool IsAtBottom()
	{
		return LogList.Scroll is not { } scroll || scroll.Offset.Y + scroll.Viewport.Height >= scroll.Extent.Height - BottomTolerance;
	}
}
