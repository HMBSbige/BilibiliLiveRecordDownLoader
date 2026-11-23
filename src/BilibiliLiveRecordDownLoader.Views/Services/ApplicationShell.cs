using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;

namespace BilibiliLiveRecordDownLoader.Views.Services;

[SuppressMessage("Design", "CA1001", Justification = "伴随进程的单例，释放后退出过程中读取 ShuttingDown 会抛 ObjectDisposedException")]
internal sealed class ApplicationShell(IServiceProvider serviceProvider, AppStartupOptions startupOptions) : IApplicationShell
{
	private readonly CancellationTokenSource _shuttingDown = new();
	private bool _isShuttingDown;

	public CancellationToken ShuttingDown => _shuttingDown.Token;

	public bool StartMinimized => startupOptions.StartMinimized;

	public MainWindow Window
	{
		get
		{
			if (field is null)
			{
				field = new MainWindow { DataContext = serviceProvider.GetRequiredService<MainWindowViewModel>() };
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

	public void BeginShutdown()
	{
		_shuttingDown.Cancel();
	}

	public void Shutdown()
	{
		_isShuttingDown = true;

		if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			desktop.Shutdown();
		}
	}

	public void Attach(Avalonia.Application application, IClassicDesktopStyleApplicationLifetime desktop)
	{
		// Windows 只在注销和关机时触发这个事件，拦下会挡住系统，又区分不出是不是系统关机
		if (OperatingSystem.IsMacOS())
		{
			desktop.ShutdownRequested += OnShutdownRequested;
		}

		if (application.TryGetFeature<IActivatableLifetime>() is { } activatable)
		{
			activatable.Activated += OnActivated;
		}
	}

	// 注销和关机时要放行，否则会拦住系统
	private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
	{
		if (_isShuttingDown || e.CloseReason is not WindowCloseReason.WindowClosing)
		{
			return;
		}

		e.Cancel = true;
		Window.Hide();
	}

	// macOS 的应用菜单、Cmd+Q 和 Dock 的“退出”不经过托盘菜单，要改走同一个退出命令先确认
	private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
	{
		MainWindowViewModel viewModel = serviceProvider.GetRequiredService<MainWindowViewModel>();

		if (_isShuttingDown || !viewModel.NeedsExitConfirmation)
		{
			return;
		}

		e.Cancel = true;
		ICommand exit = viewModel.ExitCommand;

		// 确认框打开时再按一次退出不重复进入
		if (exit.CanExecute(null))
		{
			exit.Execute(null);
		}
	}

	// macOS 上窗口隐藏后点 Dock 图标
	private void OnActivated(object? sender, ActivatedEventArgs e)
	{
		if (e.Kind is ActivationKind.Reopen)
		{
			ShowMainWindow();
		}
	}
}
