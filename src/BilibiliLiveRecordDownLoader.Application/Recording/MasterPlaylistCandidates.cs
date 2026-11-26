namespace BilibiliLiveRecordDownLoader.Application.Recording;

internal static class MasterPlaylistCandidates
{
	// 原画及以下的档位不论请求哪个 qn 都会列出，匿名时也一样；高于原画的档位要用对应的 qn 请求才会列出（原画真彩实测如此，用杜比的 qn 请求时也不列它；4K、杜比按同样规律处理）
	// 所选画质高于原画真彩、又没列出从原画真彩到所选画质的档位时，改用原画真彩的 qn 再请求一次
	// fetch 请求失败时返回空列表：第一次就失败时换个 qn 多半也一样，不再请求；第二次失败时沿用第一次列出的原画及以下档位
	public static async Task<IReadOnlyList<LiveStreamCandidate.Variant>> FetchAsync(Func<long, CancellationToken, Task<IReadOnlyList<LiveStreamCandidate.Variant>>> fetch, StreamQuality quality, CancellationToken cancellationToken)
	{
		IReadOnlyList<LiveStreamCandidate.Variant> variants = await fetch(quality.Qn, cancellationToken);

		if (variants is [] || quality.Qn <= StreamQuality.OriginalTrueColor.Qn || variants.Any(variant => variant.Qn >= StreamQuality.OriginalTrueColor.Qn && variant.Qn <= quality.Qn))
		{
			return variants;
		}

		IReadOnlyList<LiveStreamCandidate.Variant> retried = await fetch(StreamQuality.OriginalTrueColor.Qn, cancellationToken);
		return retried is [] ? variants : retried;
	}

	// 接口没给的流，不超过所选画质：匿名时各编码给的画质可能不同，接口最好的那路打不开时要先试其他编码的同一档位，再降画质
	// 比接口最差的那路还低的不要：接口的流都打不开时多半是没在推流，再试更低的档位只会拖慢重试
	public static IReadOnlyList<LiveStreamCandidate.Variant> Select(IEnumerable<LiveStreamCandidate.Variant> variants, IReadOnlyList<LiveStreamCandidate.Listed> listed, RecordingProfile profile)
	{
		long lowest = listed.Min(static candidate => candidate.Qn);
		return [.. variants.Where(variant => variant.Qn >= lowest && variant.Qn <= profile.Quality.Qn && !listed.Any(candidate => (candidate.Codec, candidate.Qn) == (variant.Codec, variant.Qn)))];
	}
}
