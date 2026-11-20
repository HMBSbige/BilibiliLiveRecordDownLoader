namespace BilibiliLiveRecordDownLoader.Views;

public class App : Avalonia.Application
{
	public override void Initialize()
	{
		AvaloniaXamlLoader.Load(this);
	}

	public override void OnFrameworkInitializationCompleted()
	{
		MainWindowViewModel viewModel = AppLocator.Current.GetRequiredService<MainWindowViewModel>();
		DataContext = viewModel;

		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			// 窗口关闭后程序留在托盘，只有托盘菜单的退出才结束进程
			desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
			Dispatcher.UIThread.Post(viewModel.RunStartupTasks, DispatcherPriority.Background);
		}

		base.OnFrameworkInitializationCompleted();
	}
}
