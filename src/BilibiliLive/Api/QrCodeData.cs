namespace BilibiliLive.Api;

internal sealed record QrCodeGenerateData(string? Url, string? QrcodeKey);

internal sealed record QrCodePollData(long Code, string? Message, string? RefreshToken);
