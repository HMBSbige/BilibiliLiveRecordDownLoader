namespace BilibiliLiveRecordDownLoader.Application.Recording;

// 一次开播期间的录制状态，下播后随录制循环一起丢弃
internal sealed class LiveRecordingSession
{
	private readonly Lock _lock = new();
	private readonly HashSet<string> _reservedStems = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<StreamEndpoint> _failedEndpoints = [];

	// 快速失败后沿用同一批候选流换线路，长时间正常录制后再重新请求，签名地址可能已过期
	public IReadOnlyList<LiveStreamCandidate>? Candidates { get; set; }

	public bool HasUntriedEndpoint => Candidates is not { } candidates || GetUntriedCandidates(candidates).Count > 0;

	// 文件可能还没建出来（FFmpeg 连上后才建），也可能已被后处理删除，只看磁盘会把同一个主干分给两次录制
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

	// 线路失败可能来自并发竞速的多个连接
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

	// 重新请求后签名会变，只用主机区分同一路流的不同线路
	private readonly record struct StreamEndpoint(StreamProtocol Protocol, StreamFormat Format, StreamCodec Codec, string Host)
	{
		public static StreamEndpoint From(LiveStreamCandidate candidate, Uri uri)
		{
			return new StreamEndpoint(candidate.Protocol, candidate.Format, candidate.Codec, uri.Authority);
		}
	}
}
