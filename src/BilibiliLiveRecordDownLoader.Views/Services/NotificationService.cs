namespace BilibiliLiveRecordDownLoader.Views.Services;

internal sealed class NotificationService(ApplicationShell shell, ILauncherService launcher) : INotificationService
{
	private WindowNotificationManager? _manager;

	public void Show(string title, string message, AppNotificationType type = AppNotificationType.Information, Uri? link = null)
	{
		Dispatcher.UIThread.Post(() => ShowCore(title, message, type, link));
	}

	private void ShowCore(string title, string message, AppNotificationType type, Uri? link)
	{
		MainWindow window = shell.Window;
		Action? openLink = link is null ? null : () => _ = OpenLinkAsync(link);

		if (window is { IsVisible: true, IsActive: true } && window.WindowState is not WindowState.Minimized)
		{
			_manager ??= new WindowNotificationManager(window) { Position = NotificationPosition.TopRight, MaxItems = 3 };
			_manager.Show(new Notification(title, message, ToNotificationType(type), onClick: openLink));
			return;
		}

		ToastWindow.Show(title, message, ToNotificationType(type), openLink ?? shell.ShowMainWindow);
	}

	private async Task OpenLinkAsync(Uri link)
	{
		try
		{
			await launcher.LaunchUriAsync(link);
		}
		catch (Exception exception)
		{
			RxState.DefaultExceptionHandler.OnNext(exception);
		}
	}

	private static NotificationType ToNotificationType(AppNotificationType type)
	{
		return type switch
		{
			AppNotificationType.Success => NotificationType.Success,
			AppNotificationType.Warning => NotificationType.Warning,
			AppNotificationType.Error => NotificationType.Error,
			AppNotificationType.Information => NotificationType.Information
		};
	}
}
