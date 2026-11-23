namespace BilibiliLive.Streams;

public abstract class LiveStreamSession : IAsyncDisposable
{
	public abstract Uri Source { get; }

	/// <summary>健康的流最长多久可以不送来数据，用于成批送来数据的流；数据持续流动时为 <see langword="null"/>。</summary>
	public virtual TimeSpan? MaxSilence => null;

	/// <summary>到目前为止 HLS 流有多少分片没来得及下载就离开了播放列表；其他流和不给分片编号的播放列表始终为 0。</summary>
	public virtual long MissedSegments => 0;

	public abstract Task<StreamEndReason> CopyToAsync(Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default);

	/// <summary>HLS 流还会把各个分片报告给 <paramref name="segments"/>，其他流什么也不报告。</summary>
	public virtual Task<StreamEndReason> CopyToAsync(Stream destination, IHlsSegmentObserver segments, IProgress<long>? progress, CancellationToken cancellationToken)
	{
		return CopyToAsync(destination, progress, cancellationToken);
	}

	public abstract ValueTask DisposeAsync();
}
