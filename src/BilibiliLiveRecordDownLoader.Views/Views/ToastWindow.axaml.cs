using Avalonia.Input;
using Avalonia.Platform;
using Symbol = FluentIcons.Common.Icon;

namespace BilibiliLiveRecordDownLoader.Views.Views;

public partial class ToastWindow : Window
{
	private const int ScreenMargin = 8;

	private static readonly TimeSpan DisplayDuration = TimeSpan.FromSeconds(6);

	// 按打开顺序排列，最新的在最下面
	private static readonly List<ToastWindow> OpenToasts = [];

	private Action? _onClick;
	private IDisposable? _timer;

	public ToastWindow()
	{
		InitializeComponent();
	}

	public static void Show(string title, string message, NotificationType type, Action onClick)
	{
		ToastWindow toast = new() { _onClick = onClick };
		toast.TitleText.Text = title;
		toast.MessageText.Text = message;
		toast.TypeIcon.Icon = type switch
		{
			NotificationType.Success => Symbol.CheckmarkCircle,
			NotificationType.Warning => Symbol.Warning,
			NotificationType.Error => Symbol.ErrorCircle,
			_ => Symbol.Info
		};
		StatusColor.SetValue(toast.TypeIcon, type);
		toast.Show();
	}

	protected override void OnOpened(EventArgs e)
	{
		base.OnOpened(e);

		OpenToasts.Add(this);
		Relayout();

		// 定位前保持透明，避免在默认位置闪现
		Opacity = 1;
		_timer = DispatcherTimer.RunOnce(Close, DisplayDuration);
	}

	protected override void OnPointerPressed(PointerPressedEventArgs e)
	{
		base.OnPointerPressed(e);
		_onClick?.Invoke();
		Close();
	}

	protected override void OnClosed(EventArgs e)
	{
		_timer?.Dispose();

		if (OpenToasts.Remove(this))
		{
			Relayout();
		}

		base.OnClosed(e);
	}

	// 从最新的开始自下而上排列，放不下的旧通知直接关闭
	private static void Relayout()
	{
		List<ToastWindow> overflow = [];
		int offset = 0;

		for (int index = OpenToasts.Count - 1; index >= 0; --index)
		{
			ToastWindow toast = OpenToasts[index];

			if ((toast.Screens.ScreenFromWindow(toast) ?? toast.Screens.Primary) is not { } screen)
			{
				continue;
			}

			PixelRect workingArea = screen.WorkingArea;
			int width = (int)(toast.Bounds.Width * screen.Scaling);
			int height = (int)(toast.Bounds.Height * screen.Scaling);
			int top = workingArea.Bottom - ScreenMargin - offset - height;

			if (top < workingArea.Y)
			{
				overflow.Add(toast);
				continue;
			}

			toast.Position = new PixelPoint(workingArea.Right - ScreenMargin - width, top);
			offset += height;
		}

		overflow.ForEach(static toast => toast.Close());
	}
}
