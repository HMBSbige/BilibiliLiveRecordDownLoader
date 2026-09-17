namespace BilibiliLive.Models;

public sealed record DanmuServerInfo(string Token, IReadOnlyList<DanmuHost> Hosts);

public sealed record DanmuHost(string Host, ushort WssPort);
