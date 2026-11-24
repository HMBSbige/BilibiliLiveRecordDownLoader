using System.Diagnostics.CodeAnalysis;

namespace BilibiliLiveRecordDownLoader.Application.Accounts;

[SuppressMessage("Design", "CA1001", Justification = "SemaphoreSlim 没有用到等待句柄，不需要释放")]
internal sealed partial class AccountAppService(BilibiliApiClient apiClient, IAppConfigManager configManager, TimeProvider timeProvider, ILogger<AccountAppService> logger) : IAccountAppService
{
	// 续期之间、续期与退出之间都要串行：两次续期会拿同一个 refresh_token 各刷新一次，续期途中退出会漏掉新会话
	private readonly SemaphoreSlim _renewal = new(1, 1);

	public async Task<string?> GetCookieAsync(CancellationToken cancellationToken = default)
	{
		return (await configManager.GetAsync(cancellationToken)).Cookie;
	}

	public async Task SetCookieAsync(string? cookie, CancellationToken cancellationToken = default)
	{
		await configManager.UpdateAsync(config =>
		{
			if (!BilibiliCookie.IsSameSession(config.Cookie, cookie))
			{
				config.RefreshToken = null;
				config.CookieExpiresAt = null;
			}

			config.Cookie = cookie;
		}, cancellationToken);
	}

	public async Task<AccountStatus?> GetStatusAsync(CancellationToken cancellationToken = default)
	{
		if (await RequestTimeout.ReportAsync(apiClient.GetCurrentUserAsync(cancellationToken)) is not { } user)
		{
			return null;
		}

		AppConfig config = await configManager.GetAsync(cancellationToken);
		return new AccountStatus(user, GetExpiry(config), config.RefreshToken is not null);
	}

	public async Task<CookieRenewal> RenewCookieAsync(CancellationToken cancellationToken = default)
	{
		await _renewal.WaitAsync(cancellationToken);

		try
		{
			return await RenewCoreAsync(cancellationToken);
		}
		finally
		{
			_renewal.Release();
		}
	}

	public Task<QrCodeLoginTicket> CreateQrCodeLoginAsync(CancellationToken cancellationToken = default)
	{
		return RequestTimeout.ReportAsync(apiClient.CreateQrCodeLoginAsync(cancellationToken));
	}

	public async Task<QrCodeLoginState> PollQrCodeLoginAsync(string key, CancellationToken cancellationToken = default)
	{
		QrCodeLoginResult result = await RequestTimeout.ReportAsync(apiClient.PollQrCodeLoginAsync(key, cancellationToken));

		if (result is QrCodeLoginResult.Succeeded { Credentials: var credentials })
		{
			await configManager.UpdateAsync(config =>
			{
				config.Cookie = credentials.Cookie;
				config.RefreshToken = credentials.RefreshToken;
				config.CookieExpiresAt = credentials.ExpiresAt;
			}, cancellationToken);
		}

		return result switch
		{
			QrCodeLoginResult.WaitingForScan => QrCodeLoginState.WaitingForScan,
			QrCodeLoginResult.WaitingForConfirm => QrCodeLoginState.WaitingForConfirm,
			QrCodeLoginResult.Expired => QrCodeLoginState.Expired,
			QrCodeLoginResult.Succeeded => QrCodeLoginState.Succeeded
		};
	}

	public async Task LogoutAsync(CancellationToken cancellationToken = default)
	{
		// 续期进行中时，等它存下新 Cookie 再读，否则服务器上注销的是旧会话，新会话仍然有效
		await _renewal.WaitAsync(cancellationToken);

		try
		{
			AppConfig config = await configManager.GetAsync(cancellationToken);

			if (config is { Cookie: { } cookie, RefreshToken: not null })
			{
				try
				{
					await RequestTimeout.ReportAsync(apiClient.LogoutAsync(cookie, cancellationToken));
				}
				catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
				{
					// Cookie 已经失效时返回的是登录页
					LogLogoutFailed(logger, exception);
				}
			}

			await configManager.UpdateAsync(static current =>
			{
				current.Cookie = null;
				current.RefreshToken = null;
				current.CookieExpiresAt = null;
			}, cancellationToken);
		}
		finally
		{
			_renewal.Release();
		}
	}

	private async Task<CookieRenewal> RenewCoreAsync(CancellationToken cancellationToken)
	{
		AppConfig config = await configManager.GetAsync(cancellationToken);

		if (!config.AutoRenewCookie || config is not { Cookie: { } cookie, RefreshToken: { } refreshToken })
		{
			return CookieRenewal.Skipped;
		}

		DateTimeOffset? expiresAt = GetExpiry(config);
		LoginCredentials renewed;

		try
		{
			CookieRefreshInfo info = await RequestTimeout.ReportAsync(apiClient.GetCookieRefreshInfoAsync(cookie, cancellationToken));

			if (!info.IsRefreshNeeded && !LoginExpiry.IsNear(expiresAt, timeProvider.GetUtcNow()))
			{
				LogRenewalNotNeeded(logger, expiresAt);
				return CookieRenewal.Skipped;
			}

			LogRenewing(logger, info.IsRefreshNeeded, expiresAt);
			// 请求发出后服务器可能已经换发，半途取消就拿不到新的 refresh_token
			renewed = await RequestTimeout.ReportAsync(apiClient.RefreshCookieAsync(cookie, refreshToken, info, CancellationToken.None));
		}
		catch (BilibiliApiException exception) when (exception.Code is BilibiliApiException.NotLoggedInCode or BilibiliApiException.RefreshTokenMismatchCode)
		{
			bool isDiscarded = false;
			await configManager.UpdateAsync(current =>
			{
				// 期间重新登录得到的新 refresh_token 不能清，失效的也不是当前会话
				if (current.RefreshToken == refreshToken)
				{
					current.RefreshToken = null;
					isDiscarded = true;
				}
			}, cancellationToken);

			if (!isDiscarded)
			{
				LogRenewalSuperseded(logger);
				return CookieRenewal.Skipped;
			}

			LogLoginExpired(logger, exception.Code);
			return CookieRenewal.LoginExpired;
		}
		catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
		{
			LogRenewalFailed(logger, exception);
			return CookieRenewal.Failed;
		}

		string? renewedCookie = null;

		try
		{
			await configManager.UpdateAndSaveAsync(current =>
			{
				// 续期期间重新登录或换了 SESSDATA 时 refresh_token 已经变了，不能覆盖
				if (current.RefreshToken == refreshToken)
				{
					current.Cookie = BilibiliCookie.Merge(current.Cookie, renewed.Cookie);
					current.RefreshToken = renewed.RefreshToken;
					current.CookieExpiresAt = renewed.ExpiresAt;
					renewedCookie = current.Cookie;
				}
			}, CancellationToken.None);
		}
		catch (Exception exception)
		{
			// 不确认，磁盘上的旧 Cookie 就仍然有效
			LogRenewedCookieNotSaved(logger, exception);
			return CookieRenewal.Failed;
		}

		if (renewedCookie is null)
		{
			LogRenewalSuperseded(logger);
			return CookieRenewal.Skipped;
		}

		LogRenewed(logger, renewed.ExpiresAt);

		try
		{
			await RequestTimeout.ReportAsync(apiClient.ConfirmCookieRefreshAsync(renewedCookie, refreshToken, CancellationToken.None));
			LogRenewalConfirmed(logger);
		}
		catch (Exception exception)
		{
			// 新 Cookie 已经存下并且可用，不确认只是旧 Cookie 不会提前失效
			LogConfirmFailed(logger, exception);
		}

		return CookieRenewal.Renewed;
	}

	// 手动填的 Cookie 没有记下过期时间，只能从 SESSDATA 推测
	private static DateTimeOffset? GetExpiry(AppConfig config)
	{
		return config.CookieExpiresAt ?? BilibiliCookie.GetSessionExpiry(config.Cookie);
	}

	[LoggerMessage(Level = LogLevel.Information, Message = "The cookie does not need renewing yet, it expires at {ExpiresAt}")]
	private static partial void LogRenewalNotNeeded(ILogger logger, DateTimeOffset? expiresAt);

	[LoggerMessage(Level = LogLevel.Information, Message = "Renewing the cookie (requested by the server: {IsRequested}), it expires at {ExpiresAt}")]
	private static partial void LogRenewing(ILogger logger, bool isRequested, DateTimeOffset? expiresAt);

	[LoggerMessage(Level = LogLevel.Information, Message = "Renewed the cookie, it now expires at {ExpiresAt}")]
	private static partial void LogRenewed(ILogger logger, DateTimeOffset? expiresAt);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to renew the cookie, retrying at the next check")]
	private static partial void LogRenewalFailed(ILogger logger, Exception exception);

	[LoggerMessage(Level = LogLevel.Warning, Message = "The login has expired (code {Code}), the refresh token is discarded")]
	private static partial void LogLoginExpired(ILogger logger, long code);

	[LoggerMessage(Level = LogLevel.Error, Message = "Failed to save the renewed cookie, the renewal is not confirmed")]
	private static partial void LogRenewedCookieNotSaved(ILogger logger, Exception exception);

	[LoggerMessage(Level = LogLevel.Information, Message = "The login changed while renewing the cookie, the renewal is abandoned")]
	private static partial void LogRenewalSuperseded(ILogger logger);

	[LoggerMessage(Level = LogLevel.Information, Message = "Confirmed the cookie renewal")]
	private static partial void LogRenewalConfirmed(ILogger logger);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to confirm the cookie renewal")]
	private static partial void LogConfirmFailed(ILogger logger, Exception exception);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to log out on the server")]
	private static partial void LogLogoutFailed(ILogger logger, Exception exception);
}
