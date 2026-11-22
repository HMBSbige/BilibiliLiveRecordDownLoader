namespace BilibiliLive.Streams;

public abstract class LiveStreamSession : IAsyncDisposable
{
	public abstract Uri Source { get; }

	/// <summary>The longest a healthy stream may go without delivering data, for streams that deliver in bursts; <see langword="null"/> when data flows continuously.</summary>
	public virtual TimeSpan? MaxSilence => null;

	/// <summary>How many segments of an HLS stream left the playlist before they could be downloaded, so far; always 0 for other streams and for playlists that do not number their segments.</summary>
	public virtual long MissedSegments => 0;

	public abstract Task<StreamEndReason> CopyToAsync(Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default);

	/// <summary>An HLS stream also reports its segments to <paramref name="segments"/>; other streams report nothing.</summary>
	public virtual Task<StreamEndReason> CopyToAsync(Stream destination, IHlsSegmentObserver segments, IProgress<long>? progress, CancellationToken cancellationToken)
	{
		return CopyToAsync(destination, progress, cancellationToken);
	}

	public abstract ValueTask DisposeAsync();
}
