namespace BilibiliLiveRecordDownLoader.ViewModels;

public sealed partial class AccountViewModel : ViewModelBase
{
	private readonly IAccountAppService _accountAppService;
	private readonly IAppConfigManager _configManager;
	private bool _isReloadingCookie;
	private int _loginCheckGeneration;

	public AccountViewModel(IAccountAppService accountAppService, IAppConfigManager configManager)
	{
		_accountAppService = accountAppService;
		_configManager = configManager;

		CanLogout = this.WhenChanged(static viewModel => viewModel.IsLoggedIn).Map(static isLoggedIn => isLoggedIn is true);

		CheckLoginCommand.DisposeWith(Disposables);
		QrCodeLoginCommand.DisposeWith(Disposables);
		LogoutCommand.DisposeWith(Disposables);
	}

	public IObservable<bool> CanLogout { get; }

	[Reactive]
	public partial bool? IsLoggedIn { get; private set; }

	[Reactive]
	public partial string? Cookie { get; set; }

	internal void ApplyConfig(AppConfig config)
	{
		Cookie = config.Cookie;

		RunLatest(this.WhenChanged(static viewModel => viewModel.Cookie).Skip(1).Unique().Keep(_ => !_isReloadingCookie), SaveCookieAsync);
	}

	internal void RefreshLoginStatus()
	{
		Forget(CheckLoginAsync);
	}

	// 启动时、手动检查、改 Cookie 和扫码登录都会发起检查，可能同时在查；以最后发起的为准
	[ReactiveCommand]
	private async Task CheckLoginAsync(CancellationToken cancellationToken)
	{
		int generation = ++_loginCheckGeneration;
		IsLoggedIn = null;

		try
		{
			ShowIfLatest(await _accountAppService.IsLoggedInAsync(cancellationToken));
		}
		catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
		{
			ShowIfLatest(false);
			throw;
		}

		void ShowIfLatest(bool isLoggedIn)
		{
			if (generation == _loginCheckGeneration)
			{
				IsLoggedIn = isLoggedIn;
			}
		}
	}

	[ReactiveCommand]
	private async Task QrCodeLoginAsync(CancellationToken cancellationToken)
	{
		await LoginWithQrCodeAsync(cancellationToken);
	}

	internal async Task<bool> LoginWithQrCodeAsync(CancellationToken cancellationToken)
	{
		if (!await AppLocator.Current.GetRequiredService<DialogHostViewModel>().ShowAsync(new QrCodeLoginViewModel(_accountAppService)))
		{
			return false;
		}

		await ReloadCookieAsync(cancellationToken);
		AppLocator.Current.GetRequiredService<INotificationService>().Show(AppLanguage.Current.Account, AppLanguage.Current.LoginSucceeded, AppNotificationType.Success);
		await CheckLoginAsync(cancellationToken);
		return true;
	}

	[ReactiveCommand(CanExecute = nameof(CanLogout))]
	private async Task LogoutAsync(CancellationToken cancellationToken)
	{
		await _accountAppService.LogoutAsync(cancellationToken);
		await ReloadCookieAsync(cancellationToken);
		IsLoggedIn = false;
	}

	// 请求从配置读 Cookie，要等写完再查
	private async Task SaveCookieAsync(string? cookie, CancellationToken cancellationToken)
	{
		await _configManager.UpdateAsync(config => config.Cookie = cookie, cancellationToken);
		await CheckLoginAsync(cancellationToken);
	}

	// 配置已经写好，读回时不再保存和检查
	private async Task ReloadCookieAsync(CancellationToken cancellationToken)
	{
		AppConfig config = await _configManager.GetAsync(cancellationToken);
		_isReloadingCookie = true;

		try
		{
			Cookie = config.Cookie;
		}
		finally
		{
			_isReloadingCookie = false;
		}
	}
}
