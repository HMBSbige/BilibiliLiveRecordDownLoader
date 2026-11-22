using System.Collections.Specialized;
using System.ComponentModel;

namespace BilibiliLiveRecordDownLoader.Views.Views;

public partial class DanmakuPanelView : UserControl
{
	// 虚拟化列表估算的高度有误差
	private const double BottomTolerance = 24;

	private LiveDanmakuViewModel? _live;
	private bool _isScrollPending;

	public DanmakuPanelView()
	{
		InitializeComponent();
		LiveList.DataContextChanged += OnLiveChanged;
		LiveList.AddHandler(ScrollViewer.ScrollChangedEvent, OnLiveScrollChanged);
	}

	protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
	{
		Attach(null);
		base.OnDetachedFromVisualTree(e);
	}

	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		Attach(LiveList.DataContext as LiveDanmakuViewModel);
	}

	private void OnLiveChanged(object? sender, EventArgs e)
	{
		Attach(LiveList.DataContext as LiveDanmakuViewModel);
	}

	private void Attach(LiveDanmakuViewModel? live)
	{
		if (_live == live)
		{
			return;
		}

		if (_live is { } previous)
		{
			previous.Entries.CollectionChanged -= OnEntriesChanged;
			previous.PropertyChanged -= OnLivePropertyChanged;
		}

		_live = live;

		if (live is not null)
		{
			live.Entries.CollectionChanged += OnEntriesChanged;
			live.PropertyChanged += OnLivePropertyChanged;
			ScheduleScrollToEnd();
		}
	}

	private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (_live is { IsFollowing: true })
		{
			ScheduleScrollToEnd();
		}
	}

	private void OnLivePropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(LiveDanmakuViewModel.IsFollowing) && _live is { IsFollowing: true })
		{
			ScheduleScrollToEnd();
		}
	}

	// 新条目只让内容变高，不算用户翻动
	private void OnLiveScrollChanged(object? sender, ScrollChangedEventArgs e)
	{
		if (_live is not { } live || e.OffsetDelta.Y is 0 || _isScrollPending || LiveList.Scroll is not ScrollViewer viewer)
		{
			return;
		}

		live.IsFollowing = viewer.Offset.Y + viewer.Viewport.Height >= viewer.Extent.Height - BottomTolerance;
	}

	// 一次刷新可能连着几次集合变化，布局完成后只滚一次
	private void ScheduleScrollToEnd()
	{
		if (_isScrollPending)
		{
			return;
		}

		_isScrollPending = true;

		Dispatcher.UIThread.Post
		(
			() =>
			{
				_isScrollPending = false;

				if (_live is { IsFollowing: true, Entries.Count: > 0 and var count })
				{
					LiveList.ScrollIntoView(count - 1);
				}
			},
			DispatcherPriority.Loaded
		);
	}
}
