namespace BilibiliLiveRecordDownLoader.Application.Recording;

// 只看这一次接口返回的内容，不为了判断再发请求
internal static class QualityRestrictions
{
	// 各编码列出的画质可能不同，合起来才是这次能录的档位；所选画质不在其中时取不高于它的最高一档，都比它高时取最低一档
	// 按所选画质而不是实际请求的画质算，开了真原画时请求的 25000 不计入，拿到普通原画不算受限
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

	// response 是同一批候选流，档位只看接口给的；主播放列表的变体只取目标档位及以上，录到它们不算受限
	public static QualityRestriction? Evaluate(LiveStreamCandidate recorded, IEnumerable<LiveStreamCandidate> response, RecordingProfile profile, bool hasCookie)
	{
		LiveStreamCandidate.Listed[] candidates = [.. response.OfType<LiveStreamCandidate.Listed>()];

		if (Target(candidates, profile.Quality) is not { } highest || recorded.Qn >= highest)
		{
			return null;
		}

		if (candidates.Where(candidate => candidate.Codec != recorded.Codec && candidate.Qn > recorded.Qn).MaxBy(static candidate => candidate.Qn) is { } better)
		{
			// 候选流按编码优先级排，排在前面的编码已经先试过
			return Rank(profile.CodecPriority, better.Codec) < Rank(profile.CodecPriority, recorded.Codec)
				? new QualityRestriction.OtherCodecUnavailable(highest, better.Codec, better.Qn)
				: new QualityRestriction.OtherCodec(highest, better.Codec, better.Qn);
		}

		return hasCookie ? new QualityRestriction.LoginMayHaveExpired(highest) : new QualityRestriction.NotLoggedIn(highest);
	}

	// 只看本地有没有配，不联网确认是否有效
	public static bool HasCookie(AppConfig config)
	{
		return !string.IsNullOrWhiteSpace(config.Cookie);
	}

	// 不在优先级里的编码排最后，和接口候选流的排序一致
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
