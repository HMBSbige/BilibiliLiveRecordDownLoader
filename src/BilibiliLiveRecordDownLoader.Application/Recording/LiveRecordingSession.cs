namespace BilibiliLiveRecordDownLoader.Application.Recording;

// 一次开播期间的录制状态，下播后丢弃
internal sealed class LiveRecordingSession
{
	private readonly Lock _lock = new();
	private readonly HashSet<string> _reservedStems = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<StreamEndpoint> _failedEndpoints = [];
	private QualityRestriction? _restriction;

	// 快速失败后沿用同一批换线路；长时间正常录制后要重新请求，签名地址可能已过期
	public CandidateBatch? Candidates { get; set; }

	public bool HasUntriedEndpoint => Candidates is not { } batch || GetUntriedCandidates(batch.Candidates).Count > 0;

	// 后处理会在后台删除或转封装上一次的文件，只看磁盘会把同一个主干分给两次录制
	public bool TryReserveStem(string stem)
	{
		lock (_lock)
		{
			return _reservedStems.Add(stem);
		}
	}

	public IReadOnlyList<LiveStreamCandidate> GetUntriedCandidates(IReadOnlyList<LiveStreamCandidate> candidates)
	{
		lock (_lock)
		{
			return
			[
				.. candidates
					.Select(candidate => candidate with { Urls = [.. candidate.Urls.Where(uri => !_failedEndpoints.Contains(StreamEndpoint.From(candidate, uri)))] })
					.Where(static candidate => candidate.Urls.Count > 0)
			];
		}
	}

	// 换线路和重新请求候选流都不清除：同一场直播重连后受限情况没变就不再提醒
	public bool TryUpdateRestriction(QualityRestriction? restriction)
	{
		lock (_lock)
		{
			if (_restriction == restriction)
			{
				return false;
			}

			_restriction = restriction;
			return true;
		}
	}

	public void MarkFailed(LiveStreamCandidate candidate, Uri endpoint)
	{
		lock (_lock)
		{
			_failedEndpoints.Add(StreamEndpoint.From(candidate, endpoint));
		}
	}

	public void Reset()
	{
		lock (_lock)
		{
			_failedEndpoints.Clear();
		}

		Candidates = null;
	}

	// 重新请求后签名会变，只用主机区分线路；变体和接口给的流、不同档位的变体都在同一主机上，要分开记
	private readonly record struct StreamEndpoint(StreamProtocol Protocol, StreamFormat Format, StreamCodec Codec, long? VariantQn, string Host)
	{
		public static StreamEndpoint From(LiveStreamCandidate candidate, Uri uri)
		{
			long? variantQn = candidate switch
			{
				LiveStreamCandidate.Listed => null,
				LiveStreamCandidate.Variant variant => variant.Qn
			};

			return new StreamEndpoint(candidate.Protocol, candidate.Format, candidate.Codec, variantQn, uri.Authority);
		}
	}
}
