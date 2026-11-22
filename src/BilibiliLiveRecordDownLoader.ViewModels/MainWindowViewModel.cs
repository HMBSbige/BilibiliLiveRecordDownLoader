namespace BilibiliLiveRecordDownLoader.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
	private bool _startupTasksStarted;

	public MainWindowViewModel()
	{
		ShowWindowCommand.DisposeWith(Disposables);
		ExitCommand.DisposeWith(Disposables);
	}

	public DialogHostViewModel Dialogs => AppLocator.Current.GetRequiredService<DialogHostViewModel>();

	public RoomListViewModel Rooms => AppLocator.Current.GetRequiredService<RoomListViewModel>();

	public TaskListViewModel Tasks => AppLocator.Current.GetRequiredService<TaskListViewModel>();

	public MediaToolsViewModel Tools => AppLocator.Current.GetRequiredService<MediaToolsViewModel>();

	public LogViewModel Logs => AppLocator.Current.GetRequiredService<LogViewModel>();

	public AccountViewModel Account => AppLocator.Current.GetRequiredService<AccountViewModel>();

	public SettingsViewModel Settings => AppLocator.Current.GetRequiredService<SettingsViewModel>();

	[Reactive]
	public partial bool IsInitialized { get; private set; }

	[Reactive]
	public partial int SelectedPageIndex { get; set; }

	[Reactive]
	public partial double WindowWidth { get; private set; } = 1280;

	[Reactive]
	public partial double WindowHeight { get; private set; } = 720;

	public void RunStartupTasks()
	{
		if (_startupTasksStarted)
		{
			return;
		}

		_startupTasksStarted = true;
		Forget(RunStartupTasksAsync);
	}

	public void Navigate(MainPage page)
	{
		SelectedPageIndex = (int)page;
	}

	public void SaveWindowSize(double width, double height)
	{
		if (!IsInitialized || width <= 0 || height <= 0)
		{
			return;
		}

		WindowWidth = width;
		WindowHeight = height;

		IAppConfigManager configManager = AppLocator.Current.GetRequiredService<IAppConfigManager>();
		Forget(cancellationToken => configManager.UpdateAsync(config => (config.WindowWidth, config.WindowHeight) = (width, height), cancellationToken).AsTask());
	}

	private async Task RunStartupTasksAsync(CancellationToken cancellationToken)
	{
		IAppConfigManager configManager = AppLocator.Current.GetRequiredService<IAppConfigManager>();
		IApplicationShell shell = AppLocator.Current.GetRequiredService<IApplicationShell>();
		AppConfig config = await configManager.GetAsync(cancellationToken);

		WindowWidth = config.WindowWidth;
		WindowHeight = config.WindowHeight;
		Settings.ApplyConfig(config);
		Account.ApplyConfig(config);
		IsInitialized = true;

		if (!shell.StartMinimized)
		{
			shell.ShowMainWindow();
		}

		await AppLocator.Current.GetRequiredService<IAppLifecycleService>().StartAsync(cancellationToken);

		Rooms.StartMonitoring();
		Tasks.StartMonitoring();
		Logs.StartMonitoring();
		Tools.RefreshFFmpegStatus();
		Account.RefreshLoginStatus();

		await Settings.Update.CheckForUpdateOnStartupAsync(config, cancellationToken);
	}

	[ReactiveCommand]
	private void ShowWindow()
	{
		AppLocator.Current.GetRequiredService<IApplicationShell>().ShowMainWindow();
	}

	[ReactiveCommand]
	private async Task ExitAsync()
	{
		IApplicationShell shell = AppLocator.Current.GetRequiredService<IApplicationShell>();
		bool isBusy = AppLocator.Current.GetRequiredService<IRoomAppService>().IsRecording || AppLocator.Current.GetRequiredService<IBackgroundTaskAppService>().HasActiveTasks;

		if (isBusy)
		{
			shell.ShowMainWindow();

			if (!await Dialogs.ConfirmAsync(AppLanguage.Current.ExitConfirmTitle, AppLanguage.Current.ExitConfirmMessage, AppLanguage.Current.Exit, true))
			{
				return;
			}
		}

		await AppLocator.Current.GetRequiredService<IAppLifecycleService>().StopAsync();
		shell.Shutdown();
	}
}
