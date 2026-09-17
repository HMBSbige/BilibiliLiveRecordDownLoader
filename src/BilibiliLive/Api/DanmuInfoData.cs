namespace BilibiliLive.Api;

internal sealed record DanmuInfoData(string? Token, IReadOnlyList<DanmuInfoHost>? HostList);

internal sealed record DanmuInfoHost(string? Host, ushort WssPort);
