using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System.Windows.Input;

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

	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);

		if (change.Property != IsVisibleProperty)
		{
			return;
		}

		if (change.GetNewValue<bool>())
		{
			_previousFocus = TopLevel.GetTopLevel(this)?.FocusManager.GetFocusedElement();
			Dispatcher.UIThread.Post(FocusFirstInput, DispatcherPriority.Loaded);
		}
		else if (_previousFocus is { } previous)
		{
			_previousFocus = null;
			previous.Focus();
		}
	}

	private void FocusFirstInput()
	{
		Control[] candidates = [.. DialogContent.GetVisualDescendants().OfType<Control>().Where(static control => control is { Focusable: true, IsEffectivelyEnabled: true, IsEffectivelyVisible: true })];

		// NumericUpDown 模板里也有 TextBox，不能让它越过排在前面的下拉框和复选框
		Control? target = Array.Find(candidates, static control => control is TextBox { TemplatedParent: null })
			?? Array.Find(candidates, static control => control is ComboBox or ToggleButton or NumericUpDown)
			?? Array.Find(candidates, static control => control is Button);

		// 按 Tab 聚焦会全选文本框里已有的内容；其他控件按默认方式聚焦，刚打开时不显示键盘焦点框
		target?.Focus(target is TextBox ? NavigationMethod.Tab : NavigationMethod.Unspecified);
	}

	// 忙碌时不关闭，但按键也不再传给下面的页面
	private void OnTopLevelKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key is not Key.Escape || DataContext is not DialogHostViewModel { Current: { } dialog })
		{
			return;
		}

		ICommand dismiss = dialog.DismissDialogCommand;

		if (dismiss.CanExecute(null))
		{
			dismiss.Execute(null);
		}

		e.Handled = true;
	}

	private void OnOverlayPressed(object? sender, PointerPressedEventArgs e)
	{
		e.Handled = true;
	}
}
