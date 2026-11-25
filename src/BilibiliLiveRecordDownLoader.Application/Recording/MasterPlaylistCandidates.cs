namespace BilibiliLiveRecordDownLoader.Application.Recording;

internal static class MasterPlaylistCandidates
{
	// 原画及以下的档位不论请求哪个 qn 都会列出，匿名时也一样；高于原画的档位要用对应的 qn 请求才会列出（原画真彩实测如此，4K、杜比按同样规律处理）
	public static long GetPlaylistQuality(RecordingProfile profile)
	{
		return profile.Quality.Qn;
	}

	// 只要比接口最好的那路高、又不超过所选画质的档位
	public static IReadOnlyList<LiveStreamCandidate.Variant> Select(IEnumerable<LiveStreamCandidate.Variant> variants, long listedBest, RecordingProfile profile)
	{
		return CandidateOrder.Sort(variants.Where(variant => variant.Qn > listedBest && variant.Qn <= profile.Quality.Qn), profile);
	}
}
