namespace BilibiliLive.Streams;

public static class LiveStreamReader
{
	/// <param name="connectTimeout">Applies to each host on its own, so one unresponsive host cannot use up the time of the others, and to every later HLS request.</param>
	/// <param name="hostFailed">Invoked for every host that fails to open, including hosts skipped before another one succeeds.</param>
	public static Task<LiveStreamSession> OpenAsync(HttpClient httpClient, LiveStreamCandidate candidate, StreamHostSelection selection, TimeSpan connectTimeout, Action<Uri, Exception>? hostFailed = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(httpClient);
		ArgumentNullException.ThrowIfNull(candidate);

		Func<Uri, CancellationToken, Task<LiveStreamSession>> open = candidate.Protocol switch
		{
			StreamProtocol.HttpStream => (uri, ct) => HttpStreamSession.OpenAsync(httpClient, uri, ct),
			StreamProtocol.HttpHls => (uri, ct) => HlsStreamSession.OpenAsync(httpClient, uri, connectTimeout, TimeProvider.System, ct),
			_ => throw new ArgumentOutOfRangeException(nameof(candidate), candidate.Protocol, "Unknown stream protocol.")
		};

		return HostSelector.OpenAsync(candidate.Urls, selection, OpenHostAsync, Random.Shared, cancellationToken);

		async Task<LiveStreamSession> OpenHostAsync(Uri uri, CancellationToken ct)
		{
			using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
			timeout.CancelAfter(connectTimeout);

			try
			{
				return await open(uri, timeout.Token);
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
