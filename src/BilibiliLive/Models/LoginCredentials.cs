namespace BilibiliLive.Models;

/// <param name="Cookie">只含响应设置的条目</param>
/// <param name="RefreshToken">续期 Cookie 要用，没有时不能续期</param>
/// <param name="ExpiresAt">SESSDATA 的过期时间</param>
public sealed record LoginCredentials(string Cookie, string? RefreshToken, DateTimeOffset? ExpiresAt);
