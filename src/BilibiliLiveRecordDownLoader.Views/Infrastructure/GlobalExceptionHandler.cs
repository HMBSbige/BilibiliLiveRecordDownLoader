namespace BilibiliLiveRecordDownLoader.Views.Infrastructure;

// 文件日志是异步写的，进程因异常结束前要刷出缓冲
internal sealed partial class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
{
	public void Install()
	{
		AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
		TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
		Dispatcher.UIThread.UnhandledException += OnUIThreadUnhandledException;
	}

	private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
	{
		LogUnhandledException(e.ExceptionObject as Exception, e.IsTerminating);

		if (e.IsTerminating)
		{
			Log.CloseAndFlush();
		}
	}

	private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
	{
		LogUnobservedTaskException(e.Exception);
		e.SetObserved();
	}

	// 界面线程的异常来自单次操作，不标记处理就会结束进程，连带中断所有录制
	private void OnUIThreadUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
	{
		LogUIThreadUnhandledException(e.Exception);
		e.Handled = true;
	}

	[LoggerMessage(Level = LogLevel.Critical, Message = "An unhandled exception occurred, terminating: {IsTerminating}")]
	private partial void LogUnhandledException(Exception? exception, bool isTerminating);

	[LoggerMessage(Level = LogLevel.Error, Message = "A task exception was not observed")]
	private partial void LogUnobservedTaskException(Exception exception);

	[LoggerMessage(Level = LogLevel.Error, Message = "An unhandled exception occurred on the UI thread")]
	private partial void LogUIThreadUnhandledException(Exception exception);
}
