namespace BilibiliLiveRecordDownLoader.ViewModels;

public sealed partial class AccountViewModel : ViewModelBase
{
	private readonly IAccountAppService _accountAppService;
	private readonly IAppConfigManager _configManager;
	private bool _isInitialized;
	private bool _isReloadingCookie;

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
		if (_isInitialized)
		{
			return;
		}

		Cookie = config.Cookie;

		// 请求从配置读 Cookie，要等写完再查
		this.WhenChanged(static viewModel => viewModel.Cookie)
			.Skip(1)
			.Unique()
			.Keep(_ => !_isReloadingCookie)
			.Map(cookie => Signal.FromAsync(cancellationToken => SaveCookieAsync(cookie, cancellationToken)))
			.SwitchTo()
			.Subscribe()
			.DisposeWith(Disposables);

		_isInitialized = true;
	}

	internal void RefreshLoginStatus()
	{
		Forget(CheckLoginAsync);
	}

	[ReactiveCommand]
	private async Task CheckLoginAsync(CancellationToken cancellationToken)
	{
		IsLoggedIn = null;

		try
		{
			IsLoggedIn = await _accountAppService.IsLoggedInAsync(cancellationToken);
		}
		catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
		{
			IsLoggedIn = false;
			throw;
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

	private async Task<RxVoid> SaveCookieAsync(string? cookie, CancellationToken cancellationToken)
	{
		try
		{
			await _configManager.UpdateAsync(config => config.Cookie = cookie, cancellationToken);
			await CheckLoginAsync(cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception exception)
		{
			RxState.DefaultExceptionHandler.OnNext(exception);
		}

		return RxVoid.Default;
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
