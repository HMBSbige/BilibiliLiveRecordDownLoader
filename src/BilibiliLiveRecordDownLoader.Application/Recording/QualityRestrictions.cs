namespace BilibiliLiveRecordDownLoader.Application.Recording;

// 只看这一次接口返回的内容，不为了判断再发请求
internal static class QualityRestrictions
{
	// 各编码列出的画质可能不同，要合起来；按所选画质而不是请求的画质算，开了真原画时拿到普通原画不算受限
	public static long? Target(IEnumerable<LiveStreamCandidate> response, StreamQuality quality)
	{
		long[] offered = [.. response.OfType<LiveStreamCandidate.Listed>().SelectMany(static candidate => candidate.AcceptQualities)];

		if (offered.Length is 0)
		{
			return null;
		}

		long selected = (long)quality;
		return offered.Where(qn => qn <= selected).DefaultIfEmpty(offered.Min()).Max();
	}

	// 档位只看接口给的；主播放列表的变体只取目标档位及以上，录到它们不算受限
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
