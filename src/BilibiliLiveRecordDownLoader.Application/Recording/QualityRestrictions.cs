namespace BilibiliLiveRecordDownLoader.Application.Recording;

internal static class QualityRestrictions
{
	// 各编码列出的画质可能不同，要合起来；和候选流排序的口径一样，以不超过所选画质的最高一档为准，都超过时取最低的一档；选哔哩哔哩默认时给什么都不算受限
	public static long? Target(IEnumerable<LiveStreamCandidate> response, StreamQuality quality)
	{
		long[] offered = [.. response.OfType<LiveStreamCandidate.Listed>().SelectMany(static candidate => candidate.AcceptQualities)];

		if (offered.Length is 0 || quality == StreamQuality.Default)
		{
			return null;
		}

		return offered.MinBy(qn => CandidateOrder.RankQuality(quality, qn));
	}

	public static QualityRestriction? Evaluate(LiveStreamCandidate recorded, IEnumerable<LiveStreamCandidate> response, RecordingProfile profile, bool hasCookie)
	{
		LiveStreamCandidate.Listed[] candidates = [.. response.OfType<LiveStreamCandidate.Listed>()];

		if (Target(candidates, profile.Quality) is not { } highest || recorded.Qn >= highest)
		{
			return null;
		}

		// 先比画质再比编码，画质比录到的高、又不超过目标的那路排在录到的这一路之前，说明已经试过、没打开
		if (candidates.Where(candidate => candidate.Codec != recorded.Codec && candidate.Qn > recorded.Qn && candidate.Qn <= highest).MaxBy(static candidate => candidate.Qn) is { } better)
		{
			return new QualityRestriction.OtherCodecUnavailable(highest, better.Codec, better.Qn);
		}

		return hasCookie ? new QualityRestriction.LoginMayHaveExpired(highest) : new QualityRestriction.NotLoggedIn(highest);
	}
}
