namespace BilibiliLive.Streams;

public static class LiveStreamReader
{
	/// <param name="connectTimeout">每个主机单独计时，打开 HLS 主机包括下载第一个分片；之后每次刷新 HLS 播放列表、每次下载分片收到响应头也受此限</param>
	/// <param name="hostFailed">每个打开失败的主机调用一次；<see cref="StreamHostSelection.FastestResponse"/> 时可能被并发调用</param>
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
