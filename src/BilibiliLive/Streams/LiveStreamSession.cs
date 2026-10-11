namespace BilibiliLive.Streams;

public abstract class LiveStreamSession : IAsyncDisposable
{
	public abstract Uri Source { get; }

	public abstract Task CopyToAsync(Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default);

	public abstract ValueTask DisposeAsync();
}
