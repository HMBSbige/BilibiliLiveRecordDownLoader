namespace BilibiliLiveRecordDownLoader.Application.Accounts;

internal sealed class AccountAppService(BilibiliApiClient apiClient, IAppConfigManager configManager) : IAccountAppService
{
	public Task<bool> IsLoggedInAsync(CancellationToken cancellationToken = default)
	{
		return apiClient.IsLoggedInAsync(cancellationToken);
	}

	public Task<QrCodeLoginTicket> CreateQrCodeLoginAsync(CancellationToken cancellationToken = default)
	{
		return apiClient.CreateQrCodeLoginAsync(cancellationToken);
	}

	public async Task<QrCodeLoginState> PollQrCodeLoginAsync(string key, CancellationToken cancellationToken = default)
	{
		QrCodeLoginResult result = await apiClient.PollQrCodeLoginAsync(key, cancellationToken);

		if (result is { State: QrCodeLoginState.Succeeded, Cookie: { Length: > 0 } cookie })
		{
			await configManager.UpdateAsync(config => config.Cookie = cookie, cancellationToken);
		}

		return result.State;
	}

	public async Task LogoutAsync(CancellationToken cancellationToken = default)
	{
		await configManager.UpdateAsync(static config => config.Cookie = null, cancellationToken);
	}
}
