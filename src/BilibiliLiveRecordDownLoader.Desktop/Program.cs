using NullLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger;

namespace BilibiliLiveRecordDownLoader.Desktop;

internal static partial class Program
{
	private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(10);

	private static readonly TimeSpan ExitingInstanceTimeout = TimeSpan.FromSeconds(15);

	private static readonly PosixSignal[] TerminationSignals = [PosixSignal.SIGTERM, PosixSignal.SIGINT, PosixSignal.SIGHUP];

	[STAThread]
	public static int Main(string[] args)
	{
		using SingleInstance singleInstance = new(AppConsts.Name);

		if (!singleInstance.TryAcquire(TimeSpan.Zero) && (singleInstance.ActivateFirstInstance() || !singleInstance.TryAcquire(ExitingInstanceTimeout)))
		{
			return 0;
		}

		PosixSignalRegistration[] signals = [];

		try
		{
			return BuildAvaloniaApp(AppStartupOptions.Parse(args))
				// 平台初始化之后才能往 UI 线程投递，容器也在那时建好
				// ReSharper disable once AccessToDisposedClosure
				.AfterSetup(_ => signals = ListenForRequests(singleInstance))
				.StartWithClassicDesktopLifetime(args);
		}
		finally
		{
			singleInstance.StopListening();
			Shutdown();

			foreach (PosixSignalRegistration signal in signals)
			{
				signal.Dispose();
			}
		}
	}

	private static AppBuilder BuildAvaloniaApp(AppStartupOptions startupOptions)
	{
		return AppBuilder.Configure<App>()
			.UsePlatformDetect()
			.UseBilibiliLiveRecordDownLoader(startupOptions)
			.LogToTrace()
			.With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.AngleEgl, Win32RenderingMode.Vulkan, Win32RenderingMode.Wgl, Win32RenderingMode.Software] })
			.With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Vulkan, X11RenderingMode.Egl, X11RenderingMode.Glx, X11RenderingMode.Software] })
			// Skia 按加载器的 Vulkan 版本（最高 1.4）检查函数入口，不看实例的版本，Avalonia 默认建的 1.1 实例不够
			.With(new VulkanOptions { VulkanInstanceCreationOptions = { VulkanVersion = new Version(1, 4, 0) } });
	}

	private static PosixSignalRegistration[] ListenForRequests(SingleInstance singleInstance)
	{
		IApplicationShell shell = AppLocator.Current.GetRequiredService<IApplicationShell>();
		_ = shell.ShuttingDown.Register(singleInstance.StopListening);
		singleInstance.StartListening(() => Dispatcher.UIThread.Post(shell.ShowMainWindow));

		// 平台初始化前在信号处理线程上首次访问 Dispatcher.UIThread 会把它定成 UI 线程，平台初始化随即失败，所以到这里才接管；此前还没有要收尾的东西
		return OperatingSystem.IsWindows() ? [] : [.. TerminationSignals.Select(signal => PosixSignalRegistration.Create(signal, OnTerminationSignal))];

		void OnTerminationSignal(PosixSignalContext context)
		{
			context.Cancel = true;
			Dispatcher.UIThread.Post(shell.Shutdown);
		}
	}

	private static void Shutdown()
	{
		// 超时后容器可能正在后台释放，那时已无法再解析服务
		IAppConfigManager? configManager = AppLocator.Current.GetService<IAppConfigManager>();
		ILoggerProvider[] loggerProviders = [.. AppLocator.Current.GetServices<ILoggerProvider>()];
		ILogger logger = AppLocator.Current.GetService<ILoggerFactory>()?.CreateLogger(typeof(Program)) ?? NullLogger.Instance;

		try
		{
			// 主循环结束后 UI 线程不再调度，等待中的续体回不来
			if (Task.Run(() => ShutdownAsync(logger)).Wait(ShutdownTimeout))
			{
				return;
			}

			LogShutdownTimedOut(logger, ShutdownTimeout);
		}
		catch (AggregateException exception)
		{
			// 只有释放容器本身出错会到这里，日志可能已经关闭
			LogDisposeFailed(logger, exception);
		}

		// 两者重复释放都是安全的
		(configManager as IDisposable)?.Dispose();

		foreach (ILoggerProvider provider in loggerProviders)
		{
			provider.Dispose();
		}
	}

	// 兜底注销、信号等不经过 ExitCommand 结束的情况
	private static async Task ShutdownAsync(ILogger logger)
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
