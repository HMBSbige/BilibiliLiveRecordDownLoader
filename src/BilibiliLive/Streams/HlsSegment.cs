namespace BilibiliLive.Streams;

/// <param name="Sequence"><see langword="null"/> when the playlist does not number its segments.</param>
internal sealed record HlsSegment(string Uri, string? MapUri, long DiscontinuitySequence, long? Sequence);
