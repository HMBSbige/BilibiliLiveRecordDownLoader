namespace BilibiliLiveRecordDownLoader.Application.Recording;

// 画质相同时先用哔哩哔哩没有转码的源流，哪怕它的编码、格式优先级靠后：转码会再损失一次画质
internal static class CandidateOrder
{
	public static IReadOnlyList<T> Sort<T>(IReadOnlyList<T> candidates, RecordingProfile profile) where T : LiveStreamCandidate
	{
		return [.. candidates.OrderBy(candidate => (RankQuality(profile.Quality, candidate.Qn), !IsSource(candidate, candidates), Rank(profile.CodecPriority, candidate.Codec), Rank(profile.FormatPriority, candidate.Format)))];
	}

	// 后缀种类不固定，所以只在同一批里找它的转码；找不到就不算源流
	public static bool IsSource(LiveStreamCandidate candidate, IReadOnlyList<LiveStreamCandidate> batch)
	{
		for (int index = 0; index < batch.Count; ++index)
		{
			if (candidate.IsSourceOf(batch[index]))
			{
				return true;
			}
		}

		return false;
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
