namespace BilibiliLiveRecordDownLoader.Application.Contracts.Accounts;

public interface IAccountAppService
{
	// 没有 Cookie 时为 null
	Task<string?> GetCookieAsync(CancellationToken cancellationToken = default);

	// 去掉首尾空白后保存，只有空白时清除；之后的请求立即改用它
	Task SetCookieAsync(string? cookie, CancellationToken cancellationToken = default);

	Task<bool> IsLoggedInAsync(CancellationToken cancellationToken = default);

	Task<QrCodeLoginTicket> CreateQrCodeLoginAsync(CancellationToken cancellationToken = default);

	Task<QrCodeLoginState> PollQrCodeLoginAsync(string key, CancellationToken cancellationToken = default);

	Task LogoutAsync(CancellationToken cancellationToken = default);
}
