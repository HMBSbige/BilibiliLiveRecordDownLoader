namespace BilibiliLiveRecordDownLoader.Application.Recording;

internal static class MasterPlaylistCandidates
{
	// 接口可能只在一个编码上给真原画，所以和主播放列表的变体一样先比画质再比编码
	public static IReadOnlyList<LiveStreamCandidate.Listed> PreferTrueOriginal(IReadOnlyList<LiveStreamCandidate.Listed> listed, RecordingProfile profile)
	{
		return profile.RequestQuality is StreamQualityConsts.TrueOriginal ? [.. listed.OrderByDescending(static candidate => candidate.Qn is StreamQualityConsts.TrueOriginal)] : listed;
	}

	// 按先后排；为空时不请求主播放列表
	public static long[] GetQualities(IReadOnlyList<LiveStreamCandidate.Listed> listed, RecordingProfile profile, bool hasCookie)
	{
		bool trueOriginal = profile.RequestQuality is StreamQualityConsts.TrueOriginal;

		// 只按首选的流判断：它连不上时不补变体，免得每次取候选流都多请求一次主播放列表
		long? restrictedTarget = profile.UseMasterPlaylistWhenRestricted && listed is [var preferred, ..] && QualityRestrictions.Evaluate(preferred, listed, profile, hasCookie) is { Highest: var highest } ? highest : null;

		return (restrictedTarget, trueOriginal) switch
		{
			({ } target, true) when target is not StreamQualityConsts.TrueOriginal => [StreamQualityConsts.TrueOriginal, target],
			({ } target, _) => [target],
			// 拿到普通原画不算受限，但主播放列表里可能有真原画，和是否登录无关
			(null, true) when listed is not [{ Qn: StreamQualityConsts.TrueOriginal }, ..] => [StreamQualityConsts.TrueOriginal],
			_ => []
		};
	}

	// mid 本应是请求者的 uid，接口目前不校验；只在要真原画时填主播的 uid，免得以后开始校验时被风控
	public static long GetMid(RecordingProfile profile, long streamerUid)
	{
		return profile.RequestQuality is StreamQualityConsts.TrueOriginal ? streamerUid : 0;
	}

	public static IReadOnlyList<LiveStreamCandidate.Variant> Select(IEnumerable<LiveStreamCandidate.Variant> variants, long[] qualities, IReadOnlyList<StreamCodec> codecPriority)
	{
		return [.. variants.Where(variant => qualities.Contains(variant.Qn)).OrderBy(variant => Array.IndexOf(qualities, variant.Qn)).ThenBy(variant => QualityRestrictions.Rank(codecPriority, variant.Codec))];
	}
}
