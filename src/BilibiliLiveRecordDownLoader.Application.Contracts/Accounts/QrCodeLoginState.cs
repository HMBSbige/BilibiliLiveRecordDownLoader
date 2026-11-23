namespace BilibiliLiveRecordDownLoader.Application.Contracts.Accounts;

public enum QrCodeLoginState
{
	WaitingForScan,
	WaitingForConfirm,
	Expired,
	Succeeded
}
