namespace BilibiliLiveRecordDownLoader.Application.Recording;

internal static class QualityRestrictions
{
	// 各编码列出的画质可能不同，要合起来；和候选流排序的口径一样，以不超过所选画质的最高一档为准，都超过时取最低的一档；选哔哩哔哩默认时给什么都不算受限
	public static StreamQuality? Target(IEnumerable<LiveStreamCandidate.Listed> response, StreamQuality quality)
	{
		if (quality == StreamQuality.Default)
		{
			return null;
		}

		long[] offered = [.. response.SelectMany(static candidate => candidate.AcceptQualities)];
		return offered is [] ? null : new StreamQuality(offered.MinBy(qn => CandidateOrder.RankQuality(quality, qn)));
	}

	public static QualityRestriction? Evaluate(LiveStreamCandidate recorded, IEnumerable<LiveStreamCandidate> response, RecordingProfile profile, bool hasCookie)
	{
		LiveStreamCandidate.Listed[] candidates = [.. response.OfType<LiveStreamCandidate.Listed>()];

		if (Target(candidates, profile.Quality) is not { } highest || recorded.Qn >= highest.Qn)
		{
			return null;
		}

		// 先比画质再比编码，画质比录到的高、又不超过目标的那路排在录到的这一路之前，说明已经试过、没打开
		if (candidates.Where(candidate => candidate.Codec != recorded.Codec && candidate.Qn > recorded.Qn && candidate.Qn <= highest.Qn).MaxBy(static candidate => candidate.Qn) is { } better)
		{
			return new QualityRestriction.OtherCodecUnavailable(highest, better.Codec, new StreamQuality(better.Qn));
		}

		return hasCookie ? new QualityRestriction.LoginMayHaveExpired(highest) : new QualityRestriction.NotLoggedIn(highest);
	}
}
