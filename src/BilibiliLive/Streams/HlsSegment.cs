namespace BilibiliLive.Streams;

public sealed record HlsSegment(string Uri, string? MapUri, long DiscontinuitySequence);
