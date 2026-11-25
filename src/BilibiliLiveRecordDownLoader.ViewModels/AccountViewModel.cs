namespace BilibiliLiveRecordDownLoader.ViewModels;

public sealed partial class AccountViewModel : ViewModelBase
{
	internal static readonly TimeSpan AutoCheckInterval = TimeSpan.FromHours(1);

	private readonly IAccountAppService _accountAppService;
	private readonly ISettingsAppService _settingsAppService;
	private readonly DialogHostViewModel _dialogs;
	private readonly INotificationService _notifications;
	private readonly TimeProvider _timeProvider;
	private bool _isReloadingCookie;
	private int _loginCheckGeneration;
	private AccountStatus? _status;
	private DateTimeOffset? _remindedExpiry;

	public AccountViewModel(IAccountAppService accountAppService, ISettingsAppService settingsAppService, DialogHostViewModel dialogs, INotificationService notifications, TimeProvider timeProvider)
	{
		_accountAppService = accountAppService;
		_settingsAppService = settingsAppService;
		_dialogs = dialogs;
		_notifications = notifications;
		_timeProvider = timeProvider;

		CanLogout = this.WhenChanged(static viewModel => viewModel.IsLoggedIn).Map(static isLoggedIn => isLoggedIn is true);
		OffersQrCodeLogin = this.WhenChanged(static viewModel => viewModel.IsLoggedIn, static viewModel => viewModel.IsRenewable, static viewModel => viewModel.IsExpiringSoon, ShouldOfferQrCodeLogin);

		CheckLoginCommand.DisposeWith(Disposables);
		QrCodeLoginCommand.DisposeWith(Disposables);
		LogoutCommand.DisposeWith(Disposables);

		OnLanguageChanged(UpdateStatusText);
	}

	public IObservable<bool> CanLogout { get; }

	public IObservable<bool> OffersQrCodeLogin { get; }

	[Reactive]
	public partial bool? IsLoggedIn { get; private set; }

	[Reactive]
	public partial string? Cookie { get; set; }

	[Reactive]
	public partial bool AutoRenewCookie { get; set; }

	[Reactive]
	public partial string? UserText { get; private set; }

	// 不知道有效期时为空
	[Reactive]
	public partial string? ExpiryText { get; private set; }

	[Reactive]
	public partial bool IsExpiringSoon { get; private set; }

	// 未登录时为空
	[Reactive]
	public partial bool? IsRenewable { get; private set; }

	internal void ApplySettings(AppSettingsDto settings)
	{
		AutoRenewCookie = settings.AutoRenewCookie;

		PersistSetting(_settingsAppService, this.WhenChanged(static viewModel => viewModel.AutoRenewCookie), static (current, value) => current with { AutoRenewCookie = value });
	}

	internal async Task LoadAsync(CancellationToken cancellationToken)
	{
		Cookie = await _accountAppService.GetCookieAsync(cancellationToken);

		RunLatest(this.WhenChanged(static viewModel => viewModel.Cookie).Skip(1).Unique().Keep(_ => !_isReloadingCookie), SaveCookieAsync);
	}

	internal void StartAutoCheck()
	{
		Forget(CheckLoginAsync);
		Forget(AutoCheckAsync);
	}

	// 剩余天数按挂钟时间算：计时器不计入睡眠时间
	private async Task AutoCheckAsync(CancellationToken cancellationToken)
	{
		using PeriodicTimer timer = new(AutoCheckInterval, _timeProvider);

		while (await timer.WaitForNextTickAsync(cancellationToken))
		{
			// 只在登录状态可能变了时重新查询，定时检查不该每小时都提示网络错误；查完再按新的有效期提醒
			if (await RenewCookieAsync(cancellationToken))
			{
				Forget(RefreshStatusAsync);
			}
			else
			{
				UpdateStatusText();
				RemindIfExpiring();
			}
		}
	}

	[ReactiveCommand]
	private async Task CheckLoginAsync(CancellationToken cancellationToken)
	{
		IsLoggedIn = null;
		await RenewCookieAsync(cancellationToken);
		await RefreshStatusAsync(cancellationToken);
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
		await RefreshStatusAsync(cancellationToken);
	}

	[ReactiveCommand(CanExecute = nameof(CanLogout))]
	private async Task LogoutAsync(CancellationToken cancellationToken)
	{
		await _accountAppService.LogoutAsync(cancellationToken);
		await ReloadCookieAsync(cancellationToken);
		++_loginCheckGeneration;
		ShowStatus(null);
	}

	/// <returns>登录状态是否可能变了</returns>
	private async Task<bool> RenewCookieAsync(CancellationToken cancellationToken)
	{
		CookieRenewal renewal = await _accountAppService.RenewCookieAsync(cancellationToken);

		if (renewal is CookieRenewal.Renewed)
		{
			// 续期在后台换了 Cookie，界面上还是旧的话，再编辑就会把旧 Cookie 写回去
			await ReloadCookieAsync(cancellationToken);
		}
		else if (renewal is CookieRenewal.LoginExpired)
		{
			_notifications.Show(AppLanguage.Current.Account, AppLanguage.Current.LoginExpired, AppNotificationType.Warning);
		}

		return renewal is CookieRenewal.Renewed or CookieRenewal.LoginExpired;
	}

	// 多处可能同时发起检查，以最后发起的为准
	private async Task RefreshStatusAsync(CancellationToken cancellationToken)
	{
		int generation = ++_loginCheckGeneration;
		IsLoggedIn = null;

		try
		{
			ShowIfLatest(await _accountAppService.GetStatusAsync(cancellationToken));
		}
		catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
		{
			ShowIfLatest(null);
			throw;
		}

		void ShowIfLatest(AccountStatus? status)
		{
			if (generation == _loginCheckGeneration)
			{
				ShowStatus(status);
			}
		}
	}

	private void ShowStatus(AccountStatus? status)
	{
		_status = status;
		IsLoggedIn = status is not null;
		UpdateStatusText();
		RemindIfExpiring();
	}

	private void UpdateStatusText()
	{
		if (_status is not { } status)
		{
			UserText = null;
			ExpiryText = null;
			IsExpiringSoon = false;
			IsRenewable = null;
			return;
		}

		DateTimeOffset now = _timeProvider.GetUtcNow();
		UserText = AppLanguage.Current.LoggedInAs.ToString(status.User.UserName, status.User.UserId.ToString(CultureInfo.InvariantCulture));
		ExpiryText = status.ExpiresAt is { } expiresAt ? AppLanguage.Current.LoginExpiresAt.ToString(FormatTime(expiresAt), Math.Max(0, ToLocalDate(expiresAt).DayNumber - ToLocalDate(now).DayNumber).ToString(CultureInfo.CurrentCulture)) : null;
		IsExpiringSoon = LoginExpiry.IsNear(status.ExpiresAt, now);
		IsRenewable = status.IsRenewable;
	}

	// 已登录时，不能续期或快过期也要能直接扫码换掉当前 Cookie；检查中状态未定，不显示
	private static bool ShouldOfferQrCodeLogin(bool? isLoggedIn, bool? isRenewable, bool isExpiringSoon)
	{
		return isLoggedIn switch
		{
			false => true,
			true => isRenewable is false || isExpiringSoon,
			null => false
		};
	}

	// 同一个过期时间在一次运行中只提醒一次
	private void RemindIfExpiring()
	{
		if (!IsExpiringSoon || _status?.ExpiresAt is not { } expiresAt || expiresAt == _remindedExpiry)
		{
			return;
		}

		_remindedExpiry = expiresAt;
		_notifications.Show(AppLanguage.Current.Account, AppLanguage.Current.LoginExpiringSoon.ToString(FormatTime(expiresAt)), AppNotificationType.Warning);
	}

	private string FormatTime(DateTimeOffset time)
	{
		return ToLocalTime(time).ToString("G", CultureInfo.CurrentCulture);
	}

	private DateOnly ToLocalDate(DateTimeOffset time)
	{
		return DateOnly.FromDateTime(ToLocalTime(time).DateTime);
	}

	private DateTimeOffset ToLocalTime(DateTimeOffset time)
	{
		return TimeZoneInfo.ConvertTime(time, _timeProvider.LocalTimeZone);
	}

	private async Task SaveCookieAsync(string? cookie, CancellationToken cancellationToken)
	{
		await _accountAppService.SetCookieAsync(cookie, cancellationToken);
		await RefreshStatusAsync(cancellationToken);
	}

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
