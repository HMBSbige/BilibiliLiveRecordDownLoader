namespace BilibiliLiveRecordDownLoader.ViewModels;

public sealed partial class AccountViewModel : ViewModelBase
{
	private readonly IAccountAppService _accountAppService;
	private readonly DialogHostViewModel _dialogs;
	private readonly INotificationService _notifications;
	private readonly TimeProvider _timeProvider;
	private bool _isReloadingCookie;
	private int _loginCheckGeneration;

	public AccountViewModel(IAccountAppService accountAppService, DialogHostViewModel dialogs, INotificationService notifications, TimeProvider timeProvider)
	{
		_accountAppService = accountAppService;
		_dialogs = dialogs;
		_notifications = notifications;
		_timeProvider = timeProvider;

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

	internal async Task LoadAsync(CancellationToken cancellationToken)
	{
		Cookie = await _accountAppService.GetCookieAsync(cancellationToken);

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
	internal async Task QrCodeLoginAsync(CancellationToken cancellationToken)
	{
		if (!await _dialogs.ShowAsync(new QrCodeLoginViewModel(_accountAppService, _timeProvider)))
		{
			return;
		}

		await ReloadCookieAsync(cancellationToken);
		_notifications.Show(AppLanguage.Current.Account, AppLanguage.Current.LoginSucceeded, AppNotificationType.Success);
		await CheckLoginAsync(cancellationToken);
	}

	[ReactiveCommand(CanExecute = nameof(CanLogout))]
	private async Task LogoutAsync(CancellationToken cancellationToken)
	{
		await _accountAppService.LogoutAsync(cancellationToken);
		await ReloadCookieAsync(cancellationToken);
		IsLoggedIn = false;
	}

	// 要等保存完再查，查的请求才会带上新的 Cookie
	private async Task SaveCookieAsync(string? cookie, CancellationToken cancellationToken)
	{
		await _accountAppService.SetCookieAsync(cookie, cancellationToken);
		await CheckLoginAsync(cancellationToken);
	}

	// 服务里已经保存好，读回时不再保存和检查
	private async Task ReloadCookieAsync(CancellationToken cancellationToken)
	{
		string? cookie = await _accountAppService.GetCookieAsync(cancellationToken);
		_isReloadingCookie = true;

		try
		{
			Cookie = cookie;
		}
		finally
		{
			_isReloadingCookie = false;
		}
	}
}
