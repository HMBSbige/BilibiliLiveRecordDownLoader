namespace BilibiliLive.Models;

internal sealed record DanmuServerInfo(string Token, IReadOnlyList<DanmuHost> Hosts);

internal sealed record DanmuHost(string Host, ushort WssPort);
