namespace BilibiliLiveRecordDownLoader.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
	private readonly IApplicationShell _shell;
	private readonly IAppLifecycleService _lifecycle;
	private readonly IAppConfigManager _configManager;
	private readonly IRoomAppService _roomAppService;
	private readonly IBackgroundTaskAppService _backgroundTasks;
	private bool _startupTasksStarted;

	public MainWindowViewModel(DialogHostViewModel dialogs, IApplicationShell shell, IAppLifecycleService lifecycle, IAppConfigManager configManager, IRoomAppService roomAppService, IBackgroundTaskAppService backgroundTasks)
	{
		Dialogs = dialogs;
		_shell = shell;
		_lifecycle = lifecycle;
		_configManager = configManager;
		_roomAppService = roomAppService;
		_backgroundTasks = backgroundTasks;

		ShowWindowCommand.DisposeWith(Disposables);
		ExitCommand.DisposeWith(Disposables);

		// 设置页可能改了 FFmpeg 路径
		this.WhenChanged(static viewModel => viewModel.IsInitialized, static viewModel => viewModel.SelectedPageIndex, static (isInitialized, page) => isInitialized && page == (int)MainPage.Tools)
			.Unique()
			.Keep(static isToolsPage => isToolsPage)
			.Subscribe(_ => Tools.RefreshFFmpegStatus())
			.DisposeWith(Disposables);
	}

	public DialogHostViewModel Dialogs { get; }

	public RoomListViewModel Rooms => AppLocator.Current.GetRequiredService<RoomListViewModel>();

	public TaskListViewModel Tasks => AppLocator.Current.GetRequiredService<TaskListViewModel>();

	public MediaToolsViewModel Tools => AppLocator.Current.GetRequiredService<MediaToolsViewModel>();

	public LogViewModel Logs => AppLocator.Current.GetRequiredService<LogViewModel>();

	public AccountViewModel Account => AppLocator.Current.GetRequiredService<AccountViewModel>();

	public SettingsViewModel Settings => AppLocator.Current.GetRequiredService<SettingsViewModel>();

	[Reactive]
	public partial bool IsInitialized { get; private set; }

	public bool NeedsExitConfirmation => _roomAppService.IsRecording || _backgroundTasks.HasActiveTasks;

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

		Forget(cancellationToken => _configManager.UpdateAsync(config => (config.WindowWidth, config.WindowHeight) = (width, height), cancellationToken).AsTask());
	}

	private async Task RunStartupTasksAsync(CancellationToken cancellationToken)
	{
		AppConfig config = await _configManager.GetAsync(cancellationToken);

		// 要在设置页读开机启动状态之前；没导入配置时接管，开机就只会启动一个空列表的新版
		if (_configManager.IsLegacyConfigImported)
		{
			AppLocator.Current.GetRequiredService<IAutoStartService>().TakeOverExisting();
		}

		WindowWidth = config.WindowWidth;
		WindowHeight = config.WindowHeight;
		Settings.ApplyConfig(config);
		// 房间列表的登录提示跟随账号页的 Cookie，要等它读好
		await Account.LoadAsync(cancellationToken);
		Rooms.ApplyConfig(config, Account.WhenChanged(static account => account.Cookie));
		IsInitialized = true;

		if (!_shell.StartMinimized)
		{
			_shell.ShowMainWindow();
		}

		await _lifecycle.StartAsync(cancellationToken);

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
		_shell.ShowMainWindow();
	}

	[ReactiveCommand]
	private async Task ExitAsync()
	{
		if (NeedsExitConfirmation)
		{
			_shell.ShowMainWindow();

			// 对话框依次显示，不先关掉当前的，确认框会排在它后面
			if (Dialogs.Current is { IsBusy: true })
			{
				return;
			}

			Dialogs.Current?.Dismiss();

			if (!await Dialogs.ConfirmAsync(AppLanguage.Current.ExitConfirmTitle, AppLanguage.Current.ExitConfirmMessage, AppLanguage.Current.Exit, true))
			{
				return;
			}
		}

		_shell.BeginShutdown();

		// Program 退出前还会再停止一次
		try
		{
			await _lifecycle.StopAsync();
		}
		finally
		{
			_shell.Shutdown();
		}
	}
}
