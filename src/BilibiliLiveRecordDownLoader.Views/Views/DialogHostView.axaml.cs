using Avalonia.Input;
using Avalonia.Interactivity;

namespace BilibiliLiveRecordDownLoader.Views.Views;

public partial class DialogHostView : UserControl
{
	public DialogHostView()
	{
		InitializeComponent();
	}

	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		TopLevel.GetTopLevel(this)?.AddHandler(KeyDownEvent, OnTopLevelKeyDown, RoutingStrategies.Tunnel);
	}

	protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
	{
		TopLevel.GetTopLevel(this)?.RemoveHandler(KeyDownEvent, OnTopLevelKeyDown);
		base.OnDetachedFromVisualTree(e);
	}

	private void OnTopLevelKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key is Key.Escape && DismissCurrent())
		{
			e.Handled = true;
		}
	}

	private void OnOverlayPressed(object? sender, PointerPressedEventArgs e)
	{
		e.Handled = true;
	}

	private void OnCloseClick(object? sender, RoutedEventArgs e)
	{
		DismissCurrent();
	}

	private bool DismissCurrent()
	{
		if (DataContext is not DialogHostViewModel { Current: { } dialog })
		{
			return false;
		}

		if (!dialog.IsBusy)
		{
			dialog.Dismiss();
		}

		return true;
	}
}
