namespace BilibiliLive.Streams;

public static class LiveStreamReader
{
	public static Task<LiveStreamSession> OpenAsync(HttpClient httpClient, LiveStreamCandidate candidate, StreamHostSelection selection, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(httpClient);
		ArgumentNullException.ThrowIfNull(candidate);

		Func<Uri, CancellationToken, Task<LiveStreamSession>> open = candidate.Protocol switch
		{
			StreamProtocol.HttpStream => (uri, ct) => HttpStreamSession.OpenAsync(httpClient, uri, ct),
			StreamProtocol.HttpHls => (uri, ct) => HlsStreamSession.OpenAsync(httpClient, uri, TimeProvider.System, ct),
			_ => throw new ArgumentOutOfRangeException(nameof(candidate), candidate.Protocol, "Unknown stream protocol.")
		};

		return HostSelector.OpenAsync(candidate.Urls, selection, open, Random.Shared, cancellationToken);
	}
}
