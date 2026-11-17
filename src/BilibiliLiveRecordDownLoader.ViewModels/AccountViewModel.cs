namespace BilibiliLiveRecordDownLoader.ViewModels;

public sealed partial class AccountViewModel : ViewModelBase
{
	private bool _isInitialized;

	public AccountViewModel()
	{
		CheckLoginCommand.DisposeWith(Disposables);
		QrCodeLoginCommand.DisposeWith(Disposables);
		LogoutCommand.DisposeWith(Disposables);
	}

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
		PersistToConfig(this.WhenChanged(static viewModel => viewModel.Cookie), static (appConfig, value) => appConfig.Cookie = value);

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
			IsLoggedIn = await AppLocator.Current.GetRequiredService<IAccountAppService>().IsLoggedInAsync(cancellationToken);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			IsLoggedIn = false;
			throw;
		}
	}

	[ReactiveCommand]
	private async Task QrCodeLoginAsync(CancellationToken cancellationToken)
	{
		if (!await AppLocator.Current.GetRequiredService<DialogHostViewModel>().ShowAsync(new QrCodeLoginViewModel()))
		{
			return;
		}

		await ReloadCookieAsync(cancellationToken);
		AppLocator.Current.GetRequiredService<INotificationService>().Show(AppLanguage.Current.Account, AppLanguage.Current.LoginSucceeded, AppNotificationType.Success);
		await CheckLoginAsync(cancellationToken);
	}

	[ReactiveCommand]
	private async Task LogoutAsync(CancellationToken cancellationToken)
	{
		await AppLocator.Current.GetRequiredService<IAccountAppService>().LogoutAsync(cancellationToken);
		await ReloadCookieAsync(cancellationToken);
		IsLoggedIn = false;
	}

	private async Task ReloadCookieAsync(CancellationToken cancellationToken)
	{
		AppConfig config = await AppLocator.Current.GetRequiredService<IAppConfigManager>().GetAsync(cancellationToken);
		Cookie = config.Cookie;
	}
}
