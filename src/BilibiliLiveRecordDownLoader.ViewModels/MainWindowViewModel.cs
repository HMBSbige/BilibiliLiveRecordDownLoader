namespace BilibiliLiveRecordDownLoader.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
	private readonly IApplicationShell _shell;
	private readonly IAppLifecycleService _lifecycle;
	private readonly ISettingsAppService _settingsAppService;
	private readonly IRoomAppService _roomAppService;
	private readonly IBackgroundTaskAppService _backgroundTasks;
	private readonly IAutoStartService _autoStart;
	private bool _startupTasksStarted;

	public MainWindowViewModel(DialogHostViewModel dialogs, IApplicationShell shell, IAppLifecycleService lifecycle, ISettingsAppService settingsAppService, IRoomAppService roomAppService, IBackgroundTaskAppService backgroundTasks, IAutoStartService autoStart)
	{
		Dialogs = dialogs;
		_shell = shell;
		_lifecycle = lifecycle;
		_settingsAppService = settingsAppService;
		_roomAppService = roomAppService;
		_backgroundTasks = backgroundTasks;
		_autoStart = autoStart;

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

	public RoomListViewModel Rooms => field ??= AppLocator.Current.GetRequiredService<RoomListViewModel>();

	public TaskListViewModel Tasks => field ??= AppLocator.Current.GetRequiredService<TaskListViewModel>();

	public MediaToolsViewModel Tools => field ??= AppLocator.Current.GetRequiredService<MediaToolsViewModel>();

	public LogViewModel Logs => field ??= AppLocator.Current.GetRequiredService<LogViewModel>();

	public AccountViewModel Account => field ??= AppLocator.Current.GetRequiredService<AccountViewModel>();

	public SettingsViewModel Settings => field ??= AppLocator.Current.GetRequiredService<SettingsViewModel>();

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

		Forget(cancellationToken => _settingsAppService.UpdateAsync(settings => settings with { WindowWidth = width, WindowHeight = height }, cancellationToken));
	}

	private async Task RunStartupTasksAsync(CancellationToken cancellationToken)
	{
		AppSettingsDto settings = await _settingsAppService.GetAsync(cancellationToken);

		// 要在设置页读开机启动状态之前；没导入配置时接管，开机就只会启动一个空列表的新版
		if (_settingsAppService.IsLegacyConfigImported)
		{
			_autoStart.TakeOverExisting();
		}

		WindowWidth = settings.WindowWidth;
		WindowHeight = settings.WindowHeight;
		Settings.ApplySettings(settings);
		// 房间列表的登录提示跟随账号页的 Cookie，要等它读好
		await Account.LoadAsync(cancellationToken);
		Rooms.ApplySettings(settings, Account.WhenChanged(static account => account.Cookie));
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
		Settings.Update.StartAutoCheck();
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
