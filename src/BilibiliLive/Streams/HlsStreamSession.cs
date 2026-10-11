namespace BilibiliLive.Streams;

internal sealed class HlsStreamSession : LiveStreamSession
{
	internal const int MaxAttempts = 3;
	internal const int SegmentHistoryCapacity = 1024;

	internal static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);
	internal static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

	private readonly HttpClient _httpClient;
	private readonly TimeProvider _timeProvider;
	private readonly PlaylistSnapshot _initialPlaylist;

	private HlsStreamSession(HttpClient httpClient, Uri source, PlaylistSnapshot initialPlaylist, TimeProvider timeProvider)
	{
		_httpClient = httpClient;
		Source = source;
		_initialPlaylist = initialPlaylist;
		_timeProvider = timeProvider;
	}

	public override Uri Source { get; }

	public static async Task<LiveStreamSession> OpenAsync(HttpClient httpClient, Uri uri, TimeProvider timeProvider, CancellationToken cancellationToken)
	{
		PlaylistSnapshot playlist = await FetchPlaylistAsync(httpClient, uri, cancellationToken);
		return new HlsStreamSession(httpClient, uri, playlist, timeProvider);
	}

	public override async Task CopyToAsync(Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(destination);

		PlaylistSnapshot snapshot = _initialPlaylist;
		string? writtenMapUri = null;
		SegmentHistory history = new(SegmentHistoryCapacity);
		using MemoryStream segment = new();

		// A new session starts at the latest init segment; older fragments belong to a moov this output will never contain
		if (snapshot.Playlist.Segments is [.., { MapUri: var latestMapUri }])
		{
			foreach (HlsSegment stale in snapshot.Playlist.Segments.Where(s => s.MapUri != latestMapUri))
			{
				history.Add(stale.Uri);
			}
		}

		while (true)
		{
			HlsPlaylist playlist = snapshot.Playlist;

			// Keyed by the playlist entry rather than the resolved URI, because refreshes may redirect to another CDN node
			foreach (HlsSegment hlsSegment in playlist.Segments.Where(s => history.Add(s.Uri)))
			{
				if (hlsSegment.MapUri != writtenMapUri)
				{
					// A changed init segment (encoder restart, new resolution) cannot follow the old moov in the same file
					if (writtenMapUri is not null)
					{
						return;
					}

					// Every refresh repeats the init segment, and writing it again would put a second moov box in the middle of the output
					if (hlsSegment.MapUri is { } mapUri)
					{
						await CopySegmentAsync(new Uri(snapshot.BaseUri, mapUri));
						writtenMapUri = mapUri;
					}
				}

				await CopySegmentAsync(new Uri(snapshot.BaseUri, hlsSegment.Uri));
			}

			if (playlist.EndList)
			{
				return;
			}

			await Task.Delay(GetPollInterval(playlist), _timeProvider, cancellationToken);
			snapshot = await RetryAsync(ct => FetchPlaylistAsync(_httpClient, Source, ct), cancellationToken);
		}

		async Task CopySegmentAsync(Uri uri)
		{
			// Buffering the whole segment keeps a retried download from writing a partial copy twice
			int length = await RetryAsync(ct => DownloadAsync(uri, segment, ct), cancellationToken);
			await destination.WriteAsync(segment.GetBuffer().AsMemory(0, length), cancellationToken);
			progress?.Report(length);
		}
	}

	public override ValueTask DisposeAsync()
	{
		return ValueTask.CompletedTask;
	}

	internal static TimeSpan GetPollInterval(HlsPlaylist playlist)
	{
		return playlist.TargetDuration is { } duration && duration > TimeSpan.Zero ? duration / 2 : DefaultPollInterval;
	}

	private static async Task<PlaylistSnapshot> FetchPlaylistAsync(HttpClient httpClient, Uri uri, CancellationToken cancellationToken)
	{
		using HttpResponseMessage response = await httpClient.GetAsync(uri, cancellationToken);
		response.EnsureSuccessStatusCode();
		string content = await response.Content.ReadAsStringAsync(cancellationToken);

		// Segments are relative to the playlist after redirects, which may point to another CDN node
		return new PlaylistSnapshot(response.RequestMessage?.RequestUri ?? uri, HlsPlaylist.Parse(content));
	}

	private async Task<int> DownloadAsync(Uri uri, MemoryStream buffer, CancellationToken cancellationToken)
	{
		buffer.SetLength(0);

		using HttpResponseMessage response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		response.EnsureSuccessStatusCode();
		await response.Content.CopyToAsync(buffer, cancellationToken);

		return checked((int)buffer.Length);
	}

	private async Task<T> RetryAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
	{
		for (int attempt = 1; ; ++attempt)
		{
			try
			{
				return await action(cancellationToken);
			}
			catch (Exception ex) when (attempt < MaxAttempts && IsTransient(ex) && !cancellationToken.IsCancellationRequested)
			{
				await Task.Delay(RetryDelay, _timeProvider, cancellationToken);
			}
		}

		static bool IsTransient(Exception ex)
		{
			return ex is HttpRequestException or IOException or TaskCanceledException { InnerException: TimeoutException };
		}
	}

	private sealed record PlaylistSnapshot(Uri BaseUri, HlsPlaylist Playlist);
}
