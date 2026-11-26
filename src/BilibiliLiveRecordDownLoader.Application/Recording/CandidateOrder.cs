namespace BilibiliLiveRecordDownLoader.Application.Recording;

// 先比画质；画质相同时先用接口给的流，主播放列表也列出请求者不一定能打开的流，只用来补接口没给的；再按编码优先级、格式优先级挑，不在优先级里的保持原来的先后
internal static class CandidateOrder
{
	public static IReadOnlyList<T> Sort<T>(IEnumerable<T> candidates, RecordingProfile profile) where T : LiveStreamCandidate
	{
		return [.. candidates.OrderBy(candidate => (RankQuality(profile.Quality, candidate.Qn), candidate is LiveStreamCandidate.Variant, Rank(profile.CodecPriority, candidate.Codec), Rank(profile.FormatPriority, candidate.Format)))];
	}

	// 越小越好：不超过所选画质的越高越好，都超过时越低越好；选哔哩哔哩默认时给什么录什么，不比画质
	public static (bool IsAboveSelection, long Distance) RankQuality(StreamQuality quality, long qn)
	{
		if (quality == StreamQuality.Default)
		{
			return (false, 0);
		}

		return qn <= quality.Qn ? (false, quality.Qn - qn) : (true, qn - quality.Qn);
	}

	private static int Rank<T>(IReadOnlyList<T> priority, T value) where T : struct, Enum
	{
		for (int index = 0; index < priority.Count; ++index)
		{
			if (EqualityComparer<T>.Default.Equals(priority[index], value))
			{
				return index;
			}
		}

		return int.MaxValue;
	}
}
