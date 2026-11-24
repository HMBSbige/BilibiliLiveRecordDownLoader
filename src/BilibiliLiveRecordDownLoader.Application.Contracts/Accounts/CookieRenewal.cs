namespace BilibiliLiveRecordDownLoader.Application.Contracts.Accounts;

public enum CookieRenewal
{
	// 开关关闭、不是扫码登录、还不需要续期，或者续期期间换了登录
	Skipped,
	Renewed,
	// 下一轮检查再试
	Failed,
	// refresh_token 已经丢弃，要重新扫码登录
	LoginExpired
}
