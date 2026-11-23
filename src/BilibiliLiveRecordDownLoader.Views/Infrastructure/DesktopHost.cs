using BilibiliLiveRecordDownLoader.Application.Contracts.Lifecycle;
using BilibiliLiveRecordDownLoader.Domain.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace BilibiliLiveRecordDownLoader.Views.Infrastructure;

public sealed partial class DesktopHost
{
	private readonly ApplicationShell _shell;

	private DesktopHost(ApplicationShell shell)
	{
		_shell = shell;
	}

	public static DesktopHost Create(Action onShuttingDown)
	{
		ApplicationShell shell = AppLocator.Current.GetRequiredService<ApplicationShell>();
		_ = shell.ShuttingDown.Register(onShuttingDown);
		return new DesktopHost(shell);
	}

	public void ShowMainWindow()
	{
		Dispatcher.UIThread.Post(_shell.ShowMainWindow);
	}

	public void RequestShutdown()
	{
		Dispatcher.UIThread.Post(_shell.Shutdown);
	}

	// 主循环结束后调用，兜底注销、信号等不经过 ExitCommand 的退出
	public static void FinishShutdown(TimeSpan timeout)
	{
		// 超时后容器可能正在后台释放，那时已无法再解析服务
		IAppConfigManager? configManager = AppLocator.Current.GetService<IAppConfigManager>();
		ILogger logger = AppLocator.Current.GetService<ILogger<DesktopHost>>() ?? NullLogger<DesktopHost>.Instance;

		if (!TryStop(logger, timeout))
		{
			(configManager as IDisposable)?.Dispose();
		}

		// 平台初始化失败时容器还没建好，要在这里刷出 Serilog 异步缓冲里的平台告警
		Log.CloseAndFlush();
	}

	private static bool TryStop(ILogger logger, TimeSpan timeout)
	{
		try
		{
			// 主循环结束后 UI 线程不再调度，等待中的续体回不来
			if (Task.Run(() => StopAsync(logger)).Wait(timeout))
			{
				return true;
			}

			LogShutdownTimedOut(logger, timeout);
		}
		catch (AggregateException exception)
		{
			LogDisposeFailed(logger, exception);
		}

		return false;
	}

	private static async Task StopAsync(ILogger logger)
	{
		try
		{
			if (AppLocator.Current.GetService<IAppLifecycleService>() is { } lifecycle)
			{
				await lifecycle.StopAsync();
			}
		}
		catch (Exception exception)
		{
			// 释放容器会关闭日志，要在 finally 之前记下
			LogShutdownFailed(logger, exception);
		}
		finally
		{
			AppLocator.GetLocator().Dispose();
		}
	}

	[LoggerMessage(Level = LogLevel.Warning, Message = "Shutdown did not finish within {Timeout}, saving config and flushing logs")]
	private static partial void LogShutdownTimedOut(ILogger logger, TimeSpan timeout);

	[LoggerMessage(Level = LogLevel.Error, Message = "Disposing services failed, saving config and flushing logs")]
	private static partial void LogDisposeFailed(ILogger logger, Exception exception);

	[LoggerMessage(Level = LogLevel.Error, Message = "Shutdown failed")]
	private static partial void LogShutdownFailed(ILogger logger, Exception exception);
}
