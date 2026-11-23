namespace BilibiliLive.Api;

internal sealed record RoomPlayInfoData(int LiveStatus, PlayUrlInfo? PlayurlInfo);

internal sealed record PlayUrlInfo(PlayUrl? Playurl);

internal sealed record PlayUrl(IReadOnlyList<PlayStream>? Stream);

internal sealed record PlayStream(string? ProtocolName, IReadOnlyList<PlayFormat>? Format);

internal sealed record PlayFormat(string? FormatName, IReadOnlyList<PlayCodec>? Codec);

internal sealed record PlayCodec(string? CodecName, long CurrentQn, IReadOnlyList<long>? AcceptQn, string? BaseUrl, IReadOnlyList<PlayUrlHost>? UrlInfo);

internal sealed record PlayUrlHost(string? Host, string? Extra);
