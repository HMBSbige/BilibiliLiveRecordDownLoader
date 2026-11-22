namespace BilibiliLiveRecordDownLoader.Application.Recording;

// 判断受限原因要按请求时的条件
internal sealed record CandidateBatch(IReadOnlyList<LiveStreamCandidate> Candidates, CandidateRequest Request)
{
	public bool HasCookie => Request.HasCookie;
}

// 影响这批候选流的设置都要放进来，变了才会重新请求
internal sealed record CandidateRequest(bool HasCookie, long RequestQuality, StreamQuality Quality, bool UseMasterPlaylistWhenRestricted, IReadOnlyList<StreamCodec> CodecPriority, IReadOnlyList<StreamFormat> FormatPriority)
{
	public static CandidateRequest Create(RecordingProfile profile, bool hasCookie)
	{
		return new CandidateRequest(hasCookie, profile.RequestQuality, profile.Quality, profile.UseMasterPlaylistWhenRestricted, profile.CodecPriority, profile.FormatPriority);
	}

	// 优先级列表每次尝试都重建，要比较内容
	public bool Equals(CandidateRequest? other)
	{
		return other is not null
			&& (HasCookie, RequestQuality, Quality, UseMasterPlaylistWhenRestricted) == (other.HasCookie, other.RequestQuality, other.Quality, other.UseMasterPlaylistWhenRestricted)
			&& CodecPriority.SequenceEqual(other.CodecPriority)
			&& FormatPriority.SequenceEqual(other.FormatPriority);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(HasCookie, RequestQuality, Quality, UseMasterPlaylistWhenRestricted);
	}
}
