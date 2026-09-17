namespace BilibiliLive.Models;

public sealed record LiveStreamCandidate(StreamProtocol Protocol, StreamFormat Format, StreamCodec Codec, long Qn, IReadOnlyList<Uri> Urls);
