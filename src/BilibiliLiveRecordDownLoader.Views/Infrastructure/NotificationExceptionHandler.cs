using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;

namespace BilibiliLiveRecordDownLoader.Views.Infrastructure;

internal sealed class NotificationExceptionHandler(INotificationService notificationService, ILogger<NotificationExceptionHandler> logger) : IDisposable
{
	private IDisposable? _subscription;

	public static Signal<Exception> ExceptionSignal { get; } = new();

	public void Install()
	{
		_subscription ??= ExceptionSignal
			.ObserveOn(RxSchedulers.MainThreadScheduler)
			.Subscribe(Handle);
	}

	public void Dispose()
	{
		_subscription?.Dispose();
	}

	// HttpClient 超时抛的 TaskCanceledException 带着 TimeoutException，它不是用户取消
	private void Handle(Exception exception)
	{
		switch (exception)
		{
			case OperationCanceledException { InnerException: TimeoutException }:
			{
				logger.LogWarning(exception, "An operation timed out.");
				notificationService.Show(AppLanguage.Current.Error, AppLanguage.Current.RequestTimedOut, AppNotificationType.Error);
				break;
			}
			case OperationCanceledException:
			{
				logger.LogTrace(exception, "The operation was canceled.");
				break;
			}
			default:
			{
				logger.LogError(exception, "An unhandled exception occurred during an operation.");
				notificationService.Show(AppLanguage.Current.Error, exception.Message, AppNotificationType.Error);
				break;
			}
		}
	}
}
