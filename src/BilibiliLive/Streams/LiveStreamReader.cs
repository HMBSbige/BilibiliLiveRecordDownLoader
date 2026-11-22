namespace BilibiliLive.Streams;

public static class LiveStreamReader
{
	/// <param name="connectTimeout">Applies to each host on its own; opening an HLS host includes downloading its first segment. Every later HLS request gets it until its response headers arrive.</param>
	/// <param name="hostFailed">Invoked for every host that fails to open, including hosts skipped before another one succeeds.</param>
	public static Task<LiveStreamSession> OpenAsync(HttpClient httpClient, LiveStreamCandidate candidate, StreamHostSelection selection, TimeSpan connectTimeout, Action<Uri, Exception>? hostFailed = null, TimeProvider? timeProvider = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(httpClient);
		ArgumentNullException.ThrowIfNull(candidate);

		TimeProvider time = timeProvider ?? TimeProvider.System;

		Func<Uri, CancellationToken, Task<LiveStreamSession>> open = candidate.Protocol switch
		{
			StreamProtocol.HttpStream => (uri, ct) => HttpStreamSession.OpenAsync(httpClient, uri, ct),
			StreamProtocol.HttpHls => (uri, ct) => HlsStreamSession.OpenAsync(httpClient, uri, connectTimeout, time, ct),
			_ => throw new ArgumentOutOfRangeException(nameof(candidate), candidate.Protocol, "Unknown stream protocol.")
		};

		return HostSelector.OpenAsync(candidate.Urls, selection, OpenHostAsync, Random.Shared, cancellationToken);

		async Task<LiveStreamSession> OpenHostAsync(Uri uri, CancellationToken ct)
		{
			using CancellationTokenSource timeout = new(connectTimeout, time);
			using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

			try
			{
				return await open(uri, linked.Token);
			}
			catch (OperationCanceledException exception) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
			{
				TimeoutException error = new($"Connecting to {uri.Host} timed out after {connectTimeout}.", exception);
				hostFailed?.Invoke(uri, error);
				throw error;
			}
			catch (Exception exception) when (!ct.IsCancellationRequested)
			{
				hostFailed?.Invoke(uri, exception);
				throw;
			}
		}
	}
}
