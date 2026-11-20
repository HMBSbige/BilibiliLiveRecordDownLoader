namespace BilibiliLiveRecordDownLoader.Views.Services;

internal sealed class NotificationService(ApplicationShell shell) : INotificationService
{
	private WindowNotificationManager? _manager;

	public void Show(string title, string message, AppNotificationType type = AppNotificationType.Information)
	{
		Dispatcher.UIThread.Post(() => ShowCore(title, message, type));
	}

	private void ShowCore(string title, string message, AppNotificationType type)
	{
		MainWindow window = shell.Window;

		if (window is { IsVisible: true, IsActive: true } && window.WindowState is not WindowState.Minimized)
		{
			_manager ??= new WindowNotificationManager(window) { Position = NotificationPosition.TopRight, MaxItems = 3 };
			_manager.Show(new Notification(title, message, ToNotificationType(type)));
			return;
		}

		// 窗口在托盘或后台时用独立的置顶小窗提醒
		ToastWindow.Show(title, message, ToNotificationType(type), shell.ShowMainWindow);
	}

	private static NotificationType ToNotificationType(AppNotificationType type)
	{
		return type switch
		{
			AppNotificationType.Success => NotificationType.Success,
			AppNotificationType.Warning => NotificationType.Warning,
			AppNotificationType.Error => NotificationType.Error,
			_ => NotificationType.Information
		};
	}
}
