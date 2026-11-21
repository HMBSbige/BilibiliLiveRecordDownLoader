namespace BilibiliLive.Streams;

internal sealed class HlsStreamSession : LiveStreamSession
{
	internal const int MaxAttempts = 3;
	internal const int SegmentHistoryCapacity = 1024;

	internal static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);
	internal static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

	private readonly HttpClient _httpClient;
	private readonly TimeSpan _requestTimeout;
	private readonly TimeProvider _timeProvider;
	private readonly PlaylistSnapshot _initialPlaylist;
	private readonly FirstSegment _firstSegment;

	private HlsStreamSession(HttpClient httpClient, Uri source, TimeSpan requestTimeout, PlaylistSnapshot initialPlaylist, FirstSegment firstSegment, TimeProvider timeProvider)
	{
		_httpClient = httpClient;
		Source = source;
		_requestTimeout = requestTimeout;
		_initialPlaylist = initialPlaylist;
		_firstSegment = firstSegment;
		_timeProvider = timeProvider;
	}

	public override Uri Source { get; }

	/// <param name="requestTimeout">Limits every later playlist refresh and segment download, because the stream client itself never times out.</param>
	public static async Task<LiveStreamSession> OpenAsync(HttpClient httpClient, Uri uri, TimeSpan requestTimeout, TimeProvider timeProvider, CancellationToken cancellationToken)
	{
		PlaylistSnapshot snapshot = await FetchPlaylistAsync(httpClient, uri, cancellationToken);
		FirstSegment firstSegment = await PrepareFirstSegmentAsync(httpClient, snapshot, cancellationToken);
		return new HlsStreamSession(httpClient, uri, requestTimeout, snapshot, firstSegment, timeProvider);
	}

	public override async Task<StreamEndReason> CopyToAsync(Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(destination);

		PlaylistSnapshot snapshot = _initialPlaylist;
		(HlsSegment first, byte[]? initData, byte[] firstData) = _firstSegment;
		SegmentHistory history = new(SegmentHistoryCapacity);
		using MemoryStream segment = new();

		// Older segments belong to a moov or timeline this output will never contain
		foreach (HlsSegment stale in snapshot.Playlist.Segments.Where(s => !IsSameEra(s, first)))
		{
			history.Add(stale.Uri);
		}

		// Both were already downloaded and validated while opening, and every refresh repeats the init segment that must only be written once
		if (initData is not null)
		{
			await WriteAsync(initData);
		}

		history.Add(first.Uri);
		await WriteAsync(firstData);

		while (true)
		{
			HlsPlaylist playlist = snapshot.Playlist;

			// Keyed by the playlist entry rather than the resolved URI, because refreshes may redirect to another CDN node
			foreach (HlsSegment hlsSegment in playlist.Segments.Where(s => history.Add(s.Uri)))
			{
				// An encoder restart or resolution change cannot continue the current file
				if (!IsSameEra(hlsSegment, first))
				{
					return StreamEndReason.Discontinuity;
				}

				await CopySegmentAsync(hlsSegment, new Uri(snapshot.BaseUri, hlsSegment.Uri));
			}

			if (playlist.EndList)
			{
				return StreamEndReason.EndOfStream;
			}

			await Task.Delay(GetPollInterval(playlist), _timeProvider, cancellationToken);
			Uri playlistUri = snapshot.Uri;
			snapshot = await RetryAsync(ct => FetchPlaylistAsync(_httpClient, playlistUri, ct), cancellationToken);
		}

		async Task CopySegmentAsync(HlsSegment hlsSegment, Uri uri)
		{
			// Buffering the whole segment keeps a retried download from writing a partial copy twice
			int length = await RetryAsync(ct => DownloadAsync(hlsSegment, uri, segment, ct), cancellationToken);
			await WriteAsync(segment.GetBuffer().AsMemory(0, length));
		}

		async Task WriteAsync(ReadOnlyMemory<byte> data)
		{
			await destination.WriteAsync(data, cancellationToken);
			progress?.Report(data.Length);
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

	// Only segments sharing an init segment and a timeline can go into the same file
	private static bool IsSameEra(HlsSegment segment, HlsSegment other)
	{
		return segment.MapUri == other.MapUri && segment.DiscontinuitySequence == other.DiscontinuitySequence;
	}

	// Refreshes reload the media playlist a master playlist pointed at, as HLS clients are meant to
	private static async Task<PlaylistSnapshot> FetchPlaylistAsync(HttpClient httpClient, Uri uri, CancellationToken cancellationToken)
	{
		(Uri baseUri, string content) = await GetPlaylistAsync(httpClient, uri, cancellationToken);

		if (HlsPlaylist.GetVariant(content) is { } variant)
		{
			uri = new Uri(baseUri, variant);
			(baseUri, content) = await GetPlaylistAsync(httpClient, uri, cancellationToken);
		}

		return new PlaylistSnapshot(uri, baseUri, HlsPlaylist.Parse(content));
	}

	private static async Task<(Uri BaseUri, string Content)> GetPlaylistAsync(HttpClient httpClient, Uri uri, CancellationToken cancellationToken)
	{
		using HttpResponseMessage response = await httpClient.GetAsync(uri, cancellationToken);
		response.EnsureSuccessStatusCode();

		// Segments are relative to the playlist after redirects, which may point to another CDN node
		return (response.RequestMessage?.RequestUri ?? uri, await response.Content.ReadAsStringAsync(cancellationToken));
	}

	// A playlist that lists nothing, or a first segment that is not media, means this host cannot be recorded from
	private static async Task<FirstSegment> PrepareFirstSegmentAsync(HttpClient httpClient, PlaylistSnapshot snapshot, CancellationToken cancellationToken)
	{
		if (snapshot.Playlist.Segments is not [.., var latest])
		{
			throw new InvalidDataException("The playlist lists no segment.");
		}

		// Starting where the latest era begins records as much as possible without mixing eras in one file
		HlsSegment first = snapshot.Playlist.Segments.First(s => IsSameEra(s, latest));
		byte[]? initData = null;

		if (first.MapUri is { } mapUri)
		{
			initData = await httpClient.GetByteArrayAsync(new Uri(snapshot.BaseUri, mapUri), cancellationToken);

			if (!MediaProbe.IsIsoBmff(initData, true))
			{
				throw new InvalidDataException($"The init segment {mapUri} is not valid fMP4.");
			}
		}

		byte[] data = await httpClient.GetByteArrayAsync(new Uri(snapshot.BaseUri, first.Uri), cancellationToken);
		EnsureMedia(first, data);

		return new FirstSegment(first, initData, data);
	}

	// Segments of a playlist with an init segment are fMP4 fragments, the others MPEG-TS
	private static void EnsureMedia(HlsSegment segment, ReadOnlySpan<byte> data)
	{
		if (segment.MapUri is null ? !MediaProbe.IsTransportStream(data) : !MediaProbe.IsIsoBmff(data, false))
		{
			throw new InvalidDataException($"The segment {segment.Uri} is not valid {(segment.MapUri is null ? "MPEG-TS" : "fMP4")}.");
		}
	}

	private async Task<int> DownloadAsync(HlsSegment segment, Uri uri, MemoryStream buffer, CancellationToken cancellationToken)
	{
		buffer.SetLength(0);

		using HttpResponseMessage response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		response.EnsureSuccessStatusCode();
		await response.Content.CopyToAsync(buffer, cancellationToken);

		int length = checked((int)buffer.Length);
		EnsureMedia(segment, buffer.GetBuffer().AsSpan(0, length));
		return length;
	}

	private async Task<T> RetryAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
	{
		for (int attempt = 1; ; ++attempt)
		{
			try
			{
				return await WithTimeoutAsync(action, cancellationToken);
			}
			catch (Exception ex) when (attempt < MaxAttempts && IsTransient(ex) && !cancellationToken.IsCancellationRequested)
			{
				await Task.Delay(RetryDelay, _timeProvider, cancellationToken);
			}
		}

		static bool IsTransient(Exception ex)
		{
			return ex is HttpRequestException or IOException or InvalidDataException or TimeoutException or TaskCanceledException { InnerException: TimeoutException };
		}
	}

	private async Task<T> WithTimeoutAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
	{
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(_requestTimeout);

		try
		{
			return await action(timeout.Token);
		}
		catch (OperationCanceledException exception) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
		{
			throw new TimeoutException($"The request timed out after {_requestTimeout}.", exception);
		}
	}

	private sealed record PlaylistSnapshot(Uri Uri, Uri BaseUri, HlsPlaylist Playlist);

	private sealed record FirstSegment(HlsSegment Segment, byte[]? InitData, byte[] Data);
}
