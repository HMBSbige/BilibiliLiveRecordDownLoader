namespace BilibiliLiveRecordDownLoader.Application.Accounts;

internal sealed class AccountAppService(BilibiliApiClient apiClient, IAppConfigManager configManager) : IAccountAppService
{
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
			await configManager.UpdateAsync(config => config.Cookie = cookie, cancellationToken);
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
		await configManager.UpdateAsync(static config => config.Cookie = null, cancellationToken);
	}
}
