namespace BilibiliLive.Models;

/// <param name="Timestamp">服务器时间的毫秒数，续期时要用它而不是本地时间</param>
public sealed record CookieRefreshInfo(bool IsRefreshNeeded, long Timestamp);
