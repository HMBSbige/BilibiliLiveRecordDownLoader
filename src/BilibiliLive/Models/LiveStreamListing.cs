namespace BilibiliLive.Models;

/// <param name="Candidates">房间在直播但没有可录制的流时为空</param>
/// <param name="QualityNames">接口给这个直播间各画质起的名称，同一 qn 在不同直播间可能叫法不同</param>
public sealed record LiveStreamListing(IReadOnlyList<LiveStreamCandidate.Listed> Candidates, IReadOnlyDictionary<long, string> QualityNames);
