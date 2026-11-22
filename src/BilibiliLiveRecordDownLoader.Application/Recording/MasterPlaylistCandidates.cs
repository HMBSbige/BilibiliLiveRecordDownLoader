namespace BilibiliLiveRecordDownLoader.Application.Recording;

// 主播放列表的变体排在接口候选流之前，都连不上时照常用接口候选流
internal static class MasterPlaylistCandidates
{
	// 要从主播放列表取的档位，按先后排；为空时不请求
	public static long[] GetQualities(IReadOnlyList<LiveStreamCandidate.Listed> listed, RecordingProfile profile, bool hasCookie)
	{
		bool trueOriginal = profile.RequestQuality is StreamQualityConsts.TrueOriginal;

		// 按首选的流判断，它不受限时录的就是它
		long? restrictedTarget = profile.UseMasterPlaylistWhenRestricted && listed is [var preferred, ..] && QualityRestrictions.Evaluate(preferred, listed, profile, hasCookie) is not null
			? QualityRestrictions.Target(listed, profile.Quality)
			: null;

		return (restrictedTarget, trueOriginal) switch
		{
			({ } target, true) => [.. new[] { StreamQualityConsts.TrueOriginal, target }.Distinct()],
			({ } target, false) => [target],
			// 拿到普通原画不算受限；接口没给真原画时主播放列表里可能有，和是否登录无关
			(null, true) when !listed.Any(static candidate => candidate.Qn is StreamQualityConsts.TrueOriginal) => [StreamQualityConsts.TrueOriginal],
			_ => []
		};
	}

	// mid 本应是请求者的 uid，接口目前不校验；只在要真原画时填主播的 uid，免得以后开始校验时被风控
	public static long GetMid(RecordingProfile profile, long streamerUid)
	{
		return profile.RequestQuality is StreamQualityConsts.TrueOriginal ? streamerUid : 0;
	}

	// 先按档位的先后，同一档内按编码优先级
	public static IReadOnlyList<LiveStreamCandidate.Variant> Select(IEnumerable<LiveStreamCandidate.Variant> variants, long[] qualities, IReadOnlyList<StreamCodec> codecPriority)
	{
		return [.. variants.Where(variant => qualities.Contains(variant.Qn)).OrderBy(variant => Array.IndexOf(qualities, variant.Qn)).ThenBy(variant => QualityRestrictions.Rank(codecPriority, variant.Codec))];
	}
}
