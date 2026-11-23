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
			DesktopHost.FinishShutdown(ShutdownTimeout);

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
			.With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.AngleEgl, Win32RenderingMode.Vulkan, Win32RenderingMode.Wgl, Win32RenderingMode.Software] })
			.With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Vulkan, X11RenderingMode.Egl, X11RenderingMode.Glx, X11RenderingMode.Software] })
			// Skia 按加载器的 Vulkan 版本（最高 1.4）检查函数入口，不看实例的版本，Avalonia 默认建的 1.1 实例不够
			.With(new VulkanOptions { VulkanInstanceCreationOptions = { VulkanVersion = new Version(1, 4, 0) } });
	}

	private static PosixSignalRegistration[] ListenForRequests(SingleInstance singleInstance)
	{
		DesktopHost host = DesktopHost.Create(singleInstance.StopListening);
		singleInstance.StartListening(host.ShowMainWindow);

		// 平台初始化前在信号处理线程上首次访问 Dispatcher.UIThread 会把它定成 UI 线程，导致平台初始化失败
		return OperatingSystem.IsWindows() ? [] : [.. TerminationSignals.Select(signal => PosixSignalRegistration.Create(signal, OnTerminationSignal))];

		void OnTerminationSignal(PosixSignalContext context)
		{
			context.Cancel = true;
			host.RequestShutdown();
		}
	}
}
