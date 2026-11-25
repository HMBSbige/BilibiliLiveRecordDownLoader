namespace BilibiliLiveRecordDownLoader.Application.Recording;

internal static class QualityRestrictions
{
	// 各编码列出的画质可能不同，要合起来；所选画质拿不到时以不超过它的最高一档为准，选哔哩哔哩默认时给什么都不算受限
	public static long? Target(IEnumerable<LiveStreamCandidate> response, StreamQuality quality)
	{
		long[] offered = [.. response.OfType<LiveStreamCandidate.Listed>().SelectMany(static candidate => candidate.AcceptQualities)];

		if (offered.Length is 0 || quality == StreamQuality.Default)
		{
			return null;
		}

		return offered.Where(qn => qn <= quality.Qn).DefaultIfEmpty(offered.Min()).Max();
	}

	public static QualityRestriction? Evaluate(LiveStreamCandidate recorded, IEnumerable<LiveStreamCandidate> response, RecordingProfile profile, bool hasCookie)
	{
		LiveStreamCandidate.Listed[] candidates = [.. response.OfType<LiveStreamCandidate.Listed>()];

		if (Target(candidates, profile.Quality) is not { } highest || recorded.Qn >= highest)
		{
			return null;
		}

		if (candidates.Where(candidate => candidate.Codec != recorded.Codec && candidate.Qn > recorded.Qn).MaxBy(static candidate => candidate.Qn) is { } better)
		{
			// candidates 按尝试的先后排列，排在录到的这一路之前说明已经试过、没打开
			return PositionOf(candidates, better) < PositionOf(candidates, recorded)
				? new QualityRestriction.OtherCodecUnavailable(highest, better.Codec, better.Qn)
				: new QualityRestriction.OtherCodec(highest, better.Codec, better.Qn);
		}

		return hasCookie ? new QualityRestriction.LoginMayHaveExpired(highest) : new QualityRestriction.NotLoggedIn(highest);
	}

	// 录到的是去掉失败线路后的副本，整条记录比不上
	private static int PositionOf(LiveStreamCandidate.Listed[] candidates, LiveStreamCandidate stream)
	{
		return Array.FindIndex(candidates, candidate => (candidate.Protocol, candidate.Format, candidate.Codec) == (stream.Protocol, stream.Format, stream.Codec));
	}

	// 要和接口候选流的排序一致
	internal static int Rank(IReadOnlyList<StreamCodec> priority, StreamCodec codec)
	{
		for (int index = 0; index < priority.Count; ++index)
		{
			if (priority[index] == codec)
			{
				return index;
			}
		}

		return int.MaxValue;
	}
}
