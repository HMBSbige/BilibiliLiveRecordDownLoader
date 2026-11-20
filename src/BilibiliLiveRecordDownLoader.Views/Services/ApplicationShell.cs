namespace BilibiliLiveRecordDownLoader.Views.Services;

internal sealed class ApplicationShell(IServiceProvider serviceProvider, AppStartupOptions startupOptions) : IApplicationShell
{
	private bool _isShuttingDown;

	public bool StartMinimized => startupOptions.StartMinimized;

	public MainWindow Window
	{
		get
		{
			if (field is null)
			{
				field = serviceProvider.GetRequiredService<MainWindow>();
				field.Closing += OnWindowClosing;
			}

			return field;
		}
	}

	public void ShowMainWindow()
	{
		MainWindow window = Window;
		window.Show();

		if (window.WindowState is WindowState.Minimized)
		{
			window.WindowState = WindowState.Normal;
		}

		window.Activate();
	}

	public void Shutdown()
	{
		_isShuttingDown = true;

		if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			desktop.Shutdown();
		}
	}

	// 用户关窗只是隐藏到托盘，录制继续进行；应用退出、注销和关机时放行，否则会拦住系统
	private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
	{
		if (_isShuttingDown || e.CloseReason is not WindowCloseReason.WindowClosing || sender is not Window window)
		{
			return;
		}

		e.Cancel = true;
		window.Hide();
	}
}
