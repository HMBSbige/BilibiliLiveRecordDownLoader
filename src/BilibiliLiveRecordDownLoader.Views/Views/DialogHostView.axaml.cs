using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace BilibiliLiveRecordDownLoader.Views.Views;

public partial class DialogHostView : UserControl
{
	private IInputElement? _previousFocus;

	public DialogHostView()
	{
		InitializeComponent();
	}

	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		// 冒泡阶段处理，下拉框等控件先用 Esc 关闭自己的弹出层
		TopLevel.GetTopLevel(this)?.AddHandler(KeyDownEvent, OnTopLevelKeyDown, RoutingStrategies.Bubble);
	}

	protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
	{
		TopLevel.GetTopLevel(this)?.RemoveHandler(KeyDownEvent, OnTopLevelKeyDown);
		base.OnDetachedFromVisualTree(e);
	}

	// 打开时记住原焦点并聚焦对话框，关闭后还原
	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);

		if (change.Property != IsVisibleProperty)
		{
			return;
		}

		if (change.GetNewValue<bool>())
		{
			_previousFocus = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
			Dispatcher.UIThread.Post(FocusFirstInput, DispatcherPriority.Loaded);
		}
		else if (_previousFocus is { } previous)
		{
			_previousFocus = null;
			previous.Focus();
		}
	}

	// 优先聚焦输入控件，没有时聚焦第一个按钮，确认框中即为“取消”
	private void FocusFirstInput()
	{
		Control[] candidates = [.. DialogContent.GetVisualDescendants().OfType<Control>().Where(static control => control is { Focusable: true, IsEffectivelyEnabled: true, IsEffectivelyVisible: true })];

		Control? target = candidates.FirstOrDefault(static control => control is TextBox)
			?? candidates.FirstOrDefault(static control => control is ComboBox or ToggleButton or NumericUpDown)
			?? candidates.FirstOrDefault(static control => control is Button);

		target?.Focus(NavigationMethod.Tab);
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
