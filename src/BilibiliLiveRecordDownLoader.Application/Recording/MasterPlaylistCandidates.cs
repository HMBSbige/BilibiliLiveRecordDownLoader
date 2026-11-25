namespace BilibiliLiveRecordDownLoader.Application.Recording;

internal static class MasterPlaylistCandidates
{
	// 接口可能只在一个编码上给原画真彩，所以先比画质再比编码
	public static IReadOnlyList<LiveStreamCandidate.Listed> PreferOriginalTrueColor(IReadOnlyList<LiveStreamCandidate.Listed> listed, RecordingProfile profile)
	{
		return profile.Quality == StreamQuality.OriginalTrueColor ? [.. listed.OrderByDescending(static candidate => candidate.Qn == StreamQuality.OriginalTrueColor.Qn)] : listed;
	}

	public static long[] GetQualities(IReadOnlyList<LiveStreamCandidate.Listed> listed, RecordingProfile profile, bool hasCookie)
	{
		// 只按首选的流判断，免得每次取候选流都多请求一次主播放列表
		if (listed is not [var preferred, ..])
		{
			return [];
		}

		long? target = QualityRestrictions.Evaluate(preferred, listed, profile, hasCookie)?.Highest;

		if (profile.Quality != StreamQuality.OriginalTrueColor)
		{
			return target is { } restricted ? [restricted] : [];
		}

		long trueColor = StreamQuality.OriginalTrueColor.Qn;

		if (preferred.Qn == trueColor)
		{
			return [];
		}

		// 主播放列表里可能有原画真彩，和是否登录无关；受限时它也拿不到就退回接口所列低于它的最高一档
		long? fallback = listed.SelectMany(static candidate => candidate.AcceptQualities).Where(qn => qn < trueColor).Max(static qn => (long?)qn);
		return target is not null && fallback is { } below && below > preferred.Qn ? [trueColor, below] : [trueColor];
	}

	// 原画及以下的档位不论请求哪个 qn 都会列出，匿名时也一样；高于原画的档位要用对应的 qn 请求才会列出（原画真彩实测如此，4K、杜比按同样规律处理）
	public static long GetPlaylistQuality(RecordingProfile profile)
	{
		return profile.Quality.Qn;
	}

	public static IReadOnlyList<LiveStreamCandidate.Variant> Select(IEnumerable<LiveStreamCandidate.Variant> variants, long[] qualities, IReadOnlyList<StreamCodec> codecPriority)
	{
		return [.. variants.Where(variant => qualities.Contains(variant.Qn)).OrderBy(variant => Array.IndexOf(qualities, variant.Qn)).ThenBy(variant => QualityRestrictions.Rank(codecPriority, variant.Codec))];
	}
}
