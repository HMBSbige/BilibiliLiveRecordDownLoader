using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;

namespace BilibiliLiveRecordDownLoader.Views.Infrastructure;

internal sealed partial class NotificationExceptionHandler(INotificationService notificationService, ILogger<NotificationExceptionHandler> logger) : IDisposable
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
				LogTimedOut(exception);
				notificationService.Show(AppLanguage.Current.Error, AppLanguage.Current.RequestTimedOut, AppNotificationType.Error);
				break;
			}
			case OperationCanceledException:
			{
				LogCanceled(exception);
				break;
			}
			default:
			{
				LogFailed(exception);
				notificationService.Show(AppLanguage.Current.Error, exception.Message, AppNotificationType.Error);
				break;
			}
		}
	}

	[LoggerMessage(Level = LogLevel.Warning, Message = "An operation timed out.")]
	private partial void LogTimedOut(Exception exception);

	[LoggerMessage(Level = LogLevel.Trace, Message = "The operation was canceled.")]
	private partial void LogCanceled(Exception exception);

	[LoggerMessage(Level = LogLevel.Error, Message = "An unhandled exception occurred during an operation.")]
	private partial void LogFailed(Exception exception);
}
