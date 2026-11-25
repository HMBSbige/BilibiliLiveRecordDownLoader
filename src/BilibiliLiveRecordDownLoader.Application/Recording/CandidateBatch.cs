namespace BilibiliLiveRecordDownLoader.Application.Recording;

// QualityNames 是接口和主播放列表给这个直播间各画质起的名称
internal sealed record CandidateBatch(IReadOnlyList<LiveStreamCandidate> Candidates, IReadOnlyDictionary<long, string> QualityNames, CandidateRequest Request);

// 影响这批候选流的设置都要放进来，变了才会重新请求
internal sealed record CandidateRequest(bool HasCookie, StreamQuality Quality, IReadOnlyList<StreamCodec> CodecPriority, IReadOnlyList<StreamFormat> FormatPriority)
{
	public static CandidateRequest Create(RecordingProfile profile, bool hasCookie)
	{
		return new CandidateRequest(hasCookie, profile.Quality, profile.CodecPriority, profile.FormatPriority);
	}

	// 优先级列表每次尝试都重建，要比较内容
	public bool Equals(CandidateRequest? other)
	{
		return other is not null
			&& (HasCookie, Quality) == (other.HasCookie, other.Quality)
			&& CodecPriority.SequenceEqual(other.CodecPriority)
			&& FormatPriority.SequenceEqual(other.FormatPriority);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(HasCookie, Quality);
	}
}
