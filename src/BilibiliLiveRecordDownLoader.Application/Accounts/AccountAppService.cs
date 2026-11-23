namespace BilibiliLiveRecordDownLoader.Application.Accounts;

internal sealed class AccountAppService(BilibiliApiClient apiClient, IAppConfigManager configManager) : IAccountAppService
{
	public async Task<string?> GetCookieAsync(CancellationToken cancellationToken = default)
	{
		return (await configManager.GetAsync(cancellationToken)).Cookie;
	}

	public async Task SetCookieAsync(string? cookie, CancellationToken cancellationToken = default)
	{
		await configManager.UpdateAsync(config => config.Cookie = cookie, cancellationToken);
	}

	public Task<bool> IsLoggedInAsync(CancellationToken cancellationToken = default)
	{
		return RequestTimeout.ReportAsync(apiClient.IsLoggedInAsync(cancellationToken));
	}

	public Task<QrCodeLoginTicket> CreateQrCodeLoginAsync(CancellationToken cancellationToken = default)
	{
		return RequestTimeout.ReportAsync(apiClient.CreateQrCodeLoginAsync(cancellationToken));
	}

	public async Task<QrCodeLoginState> PollQrCodeLoginAsync(string key, CancellationToken cancellationToken = default)
	{
		QrCodeLoginResult result = await RequestTimeout.ReportAsync(apiClient.PollQrCodeLoginAsync(key, cancellationToken));

		if (result is QrCodeLoginResult.Succeeded { Cookie: var cookie })
		{
			await SetCookieAsync(cookie, cancellationToken);
		}

		return result switch
		{
			QrCodeLoginResult.WaitingForScan => QrCodeLoginState.WaitingForScan,
			QrCodeLoginResult.WaitingForConfirm => QrCodeLoginState.WaitingForConfirm,
			QrCodeLoginResult.Expired => QrCodeLoginState.Expired,
			QrCodeLoginResult.Succeeded => QrCodeLoginState.Succeeded
		};
	}

	public Task LogoutAsync(CancellationToken cancellationToken = default)
	{
		return SetCookieAsync(null, cancellationToken);
	}
}
