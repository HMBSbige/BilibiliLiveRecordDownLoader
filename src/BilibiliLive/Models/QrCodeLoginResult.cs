namespace BilibiliLive.Models;

public closed record QrCodeLoginResult
{
	public sealed record WaitingForScan : QrCodeLoginResult;

	public sealed record WaitingForConfirm : QrCodeLoginResult;

	public sealed record Expired : QrCodeLoginResult;

	public sealed record Succeeded(string Cookie) : QrCodeLoginResult;
}
