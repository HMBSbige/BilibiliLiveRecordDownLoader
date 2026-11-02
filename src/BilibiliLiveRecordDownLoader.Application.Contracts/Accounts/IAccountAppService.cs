namespace BilibiliLiveRecordDownLoader.Application.Contracts.Accounts;

public interface IAccountAppService
{
	Task<bool> IsLoggedInAsync(CancellationToken cancellationToken = default);

	Task<QrCodeLoginTicket> CreateQrCodeLoginAsync(CancellationToken cancellationToken = default);

	Task<QrCodeLoginState> PollQrCodeLoginAsync(string key, CancellationToken cancellationToken = default);

	Task LogoutAsync(CancellationToken cancellationToken = default);
}
