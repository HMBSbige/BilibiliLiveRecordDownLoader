namespace BilibiliLive.Models;

public sealed record QrCodeLoginResult(QrCodeLoginState State, string? Cookie = null);
