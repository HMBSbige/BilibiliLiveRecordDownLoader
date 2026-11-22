namespace BilibiliLiveRecordDownLoader.Desktop;

internal static class Program
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

		PosixSignalRegistration[] signals = OperatingSystem.IsWindows() ? [] : [.. TerminationSignals.Select(static signal => PosixSignalRegistration.Create(signal, OnTerminationSignal))];

		try
		{
			return BuildAvaloniaApp(AppStartupOptions.Parse(args), singleInstance).StartWithClassicDesktopLifetime(args);
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

	private static AppBuilder BuildAvaloniaApp(AppStartupOptions startupOptions, SingleInstance singleInstance)
	{
		return AppBuilder.Configure<App>()
			.UsePlatformDetect()
			.UseBilibiliLiveRecordDownLoader(startupOptions)
			// 平台初始化之后才能把激活请求投递到 UI 线程，容器也在那时建好
			.AfterSetup(_ => ListenForActivation(singleInstance))
			.LogToTrace()
			.With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.AngleEgl, Win32RenderingMode.Vulkan, Win32RenderingMode.Wgl, Win32RenderingMode.Software] })
			.With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Vulkan, X11RenderingMode.Egl, X11RenderingMode.Glx, X11RenderingMode.Software] })
			// Skia requires the entry points of the loader's Vulkan version, up to 1.4, not the instance's
			.With(new VulkanOptions { VulkanInstanceCreationOptions = { VulkanVersion = new Version(1, 4, 0) } });
	}

	private static void ListenForActivation(SingleInstance singleInstance)
	{
		IApplicationShell shell = AppLocator.Current.GetRequiredService<IApplicationShell>();
		_ = shell.ShuttingDown.Register(singleInstance.StopListening);
		singleInstance.StartListening(() => Dispatcher.UIThread.Post(shell.ShowMainWindow));
	}

	private static void Shutdown()
	{
		// 超时后容器可能正在后台释放，那时已无法再解析服务
		IAppConfigManager? configManager = AppLocator.Current.GetService<IAppConfigManager>();
		ILoggerProvider[] loggerProviders = [.. AppLocator.Current.GetServices<ILoggerProvider>()];
		ILogger? logger = AppLocator.Current.GetService<ILoggerFactory>()?.CreateLogger(typeof(Program));

		try
		{
			// 主循环结束后 UI 线程不再调度，等待中的续体回不来
			if (Task.Run(() => ShutdownAsync(logger)).Wait(ShutdownTimeout))
			{
				return;
			}

			logger?.LogWarning("Shutdown did not finish within {Timeout}, saving config and flushing logs", ShutdownTimeout);
		}
		catch (AggregateException exception)
		{
			// 只有释放容器本身出错会到这里，日志可能已经关闭
			logger?.LogError(exception, "Disposing services failed, saving config and flushing logs");
		}

		// 两者重复释放都是安全的
		(configManager as IDisposable)?.Dispose();

		foreach (ILoggerProvider provider in loggerProviders)
		{
			provider.Dispose();
		}
	}

	// 兜底注销、信号等不经过 ExitCommand 结束的情况
	private static async Task ShutdownAsync(ILogger? logger)
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
			logger?.LogError(exception, "Shutdown failed");
		}
		finally
		{
			AppLocator.GetLocator().Dispose();
		}
	}

	// 与托盘退出走同一条路径，让录制正常收尾
	private static void OnTerminationSignal(PosixSignalContext context)
	{
		context.Cancel = true;
		Dispatcher.UIThread.Post(static () => AppLocator.Current.GetRequiredService<IApplicationShell>().Shutdown());
	}
}
