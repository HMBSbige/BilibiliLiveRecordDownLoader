namespace BilibiliLive.Api;

internal sealed record CookieInfoData(bool Refresh, long Timestamp);

internal sealed record CookieRefreshData(string? RefreshToken);
