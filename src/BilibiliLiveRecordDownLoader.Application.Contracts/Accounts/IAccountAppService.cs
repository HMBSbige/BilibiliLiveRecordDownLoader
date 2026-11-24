namespace BilibiliLiveRecordDownLoader.Application.Contracts.Accounts;

public interface IAccountAppService
{
	Task<string?> GetCookieAsync(CancellationToken cancellationToken = default);

	// SESSDATA 变了就是另一个会话，扫码登录的 refresh_token 和有效期随之丢弃
	Task SetCookieAsync(string? cookie, CancellationToken cancellationToken = default);

	// 未登录时为空
	Task<AccountStatus?> GetStatusAsync(CancellationToken cancellationToken = default);

	// 只续期扫码登录的会话；失败只记日志，不抛出
	Task<CookieRenewal> RenewCookieAsync(CancellationToken cancellationToken = default);

	Task<QrCodeLoginTicket> CreateQrCodeLoginAsync(CancellationToken cancellationToken = default);

	Task<QrCodeLoginState> PollQrCodeLoginAsync(string key, CancellationToken cancellationToken = default);

	// 只有扫码登录的会话才在服务器上注销：手动填的 Cookie 可能和浏览器共用
	Task LogoutAsync(CancellationToken cancellationToken = default);
}
