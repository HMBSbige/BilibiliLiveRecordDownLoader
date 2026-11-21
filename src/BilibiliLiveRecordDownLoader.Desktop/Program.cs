namespace BilibiliLiveRecordDownLoader.Desktop;

internal static class Program
{
	private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(10);

	// 旧实例正在退出时，等它收尾后再启动
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
			// 平台初始化之后才能把激活请求投递到 UI 线程
			.AfterSetup(_ => singleInstance.StartListening(static () => Dispatcher.UIThread.Post(static () => AppLocator.Current.GetRequiredService<IApplicationShell>().ShowMainWindow())))
			.LogToTrace()
			.With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.AngleEgl, Win32RenderingMode.Vulkan, Win32RenderingMode.Wgl, Win32RenderingMode.Software] })
			.With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Vulkan, X11RenderingMode.Egl, X11RenderingMode.Glx, X11RenderingMode.Software] })
			// Skia in SkiaSharp 4 validates Vulkan entry points up to 1.4 by the loader version, so Avalonia's default 1.1 instance is not enough.
			.With(new VulkanOptions { VulkanInstanceCreationOptions = { VulkanVersion = new Version(1, 4, 0) } });
	}

	private static void Shutdown()
	{
		// 超时后容器可能正在后台释放，那时已无法再解析服务，所以先取出来
		IAppConfigManager? configManager = AppLocator.Current.GetService<IAppConfigManager>();
		ILoggerProvider[] loggerProviders = [.. AppLocator.Current.GetServices<ILoggerProvider>()];
		ILogger? logger = AppLocator.Current.GetService<ILoggerFactory>()?.CreateLogger(typeof(Program));

		// 主循环结束后 UI 线程不再调度，等待中的续体回不来，清理放到线程池上
		if (Task.Run(ShutdownAsync).Wait(ShutdownTimeout))
		{
			return;
		}

		logger?.LogWarning("Shutdown did not finish within {Timeout}, saving config and flushing logs", ShutdownTimeout);

		// 容器没有释放完，配置的同步写盘和异步日志的刷新要在进程退出前补上；两者重复释放都是安全的
		(configManager as IDisposable)?.Dispose();

		foreach (ILoggerProvider provider in loggerProviders)
		{
			provider.Dispose();
		}
	}

	// 正常退出时 ExitCommand 已经停止过，这里兜底系统注销、信号等直接结束生命周期的情况
	private static async Task ShutdownAsync()
	{
		if (AppLocator.Current.GetService<IAppLifecycleService>() is { } lifecycle)
		{
			await lifecycle.StopAsync();
		}

		AppLocator.GetLocator().Dispose();
	}

	// 与托盘退出走同一条路径，让录制正常收尾
	private static void OnTerminationSignal(PosixSignalContext context)
	{
		context.Cancel = true;
		Dispatcher.UIThread.Post(static () => AppLocator.Current.GetRequiredService<IApplicationShell>().Shutdown());
	}
}
