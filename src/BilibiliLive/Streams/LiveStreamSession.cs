namespace BilibiliLive.Streams;

public abstract class LiveStreamSession : IAsyncDisposable
{
	public abstract Uri Source { get; }

	/// <summary>健康的流最长可以多久不送数据；数据持续流动时为 <see langword="null"/></summary>
	public virtual TimeSpan? MaxSilence => null;

	public virtual long MissedSegments => 0;

	public abstract Task<StreamEndReason> CopyToAsync(Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default);

	public virtual Task<StreamEndReason> CopyToAsync(Stream destination, IHlsSegmentObserver segments, IProgress<long>? progress, CancellationToken cancellationToken)
	{
		return CopyToAsync(destination, progress, cancellationToken);
	}

	public abstract ValueTask DisposeAsync();
}
