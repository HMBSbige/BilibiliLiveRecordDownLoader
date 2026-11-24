namespace BilibiliLiveRecordDownLoader.Application.Contracts.Accounts;

public static class LoginExpiry
{
	// 剩下的时间不到这么久就强制续期，续不上时提醒重新登录
	public static TimeSpan WarningPeriod { get; } = TimeSpan.FromDays(7);

	public static bool IsNear(DateTimeOffset? expiresAt, DateTimeOffset now)
	{
		return expiresAt - now < WarningPeriod;
	}
}
