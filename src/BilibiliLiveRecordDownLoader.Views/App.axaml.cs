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
			desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
			AppLocator.Current.GetRequiredService<ApplicationShell>().Attach(this, desktop);
			Dispatcher.UIThread.Post(viewModel.RunStartupTasks, DispatcherPriority.Background);
		}

		base.OnFrameworkInitializationCompleted();
	}
}
