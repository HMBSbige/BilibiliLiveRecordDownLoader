namespace BilibiliLive.Streams;

public abstract class LiveStreamSession : IAsyncDisposable
{
	public abstract Uri Source { get; }

	public abstract Task<StreamEndReason> CopyToAsync(Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default);

	/// <summary>An HLS stream also reports its segments to <paramref name="segments"/>; other streams report nothing.</summary>
	public virtual Task<StreamEndReason> CopyToAsync(Stream destination, IHlsSegmentObserver segments, IProgress<long>? progress, CancellationToken cancellationToken)
	{
		return CopyToAsync(destination, progress, cancellationToken);
	}

	public abstract ValueTask DisposeAsync();
}
