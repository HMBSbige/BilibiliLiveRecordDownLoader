namespace BilibiliLiveRecordDownLoader.Application.Contracts.Accounts;

/// <param name="ExpiresAt">不知道时为空</param>
/// <param name="IsRenewable">扫码登录的会话才能续期</param>
public sealed record AccountStatus(CurrentUser User, DateTimeOffset? ExpiresAt, bool IsRenewable);
