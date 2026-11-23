using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;

namespace BilibiliLive.Streams;

internal sealed class HlsStreamSession : LiveStreamSession
{
	internal const int MaxAttempts = 3;

	// Enough to catch up after a slow segment without flooding a struggling node
	internal const int MaxConcurrentDownloads = 3;

	internal static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);
	internal static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

	private const int SegmentHistoryCapacity = 1024;
	private const int DefaultSegmentCapacity = 1 << 20;
	private const int MaxInitialSegmentCapacity = 64 << 20;

	private readonly HttpClient _httpClient;
	private readonly TimeSpan _requestTimeout;
	private readonly TimeProvider _timeProvider;
	private readonly PlaylistSnapshot _initialPlaylist;
	private readonly FirstSegment _firstSegment;
	private long _missedSegments;

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

	// The listing gap (a target duration and a poll interval) plus one fault in full: a failed attempt, the retry delay and a retry as long, since the idle watchdog only backs up the session's own timeouts
	// A refresh fault fits as long as an attempt takes at least two request timeouts
	public override TimeSpan? MaxSilence
	{
		get
		{
			HlsPlaylist playlist = _initialPlaylist.Playlist;
			TimeSpan attempt = _requestTimeout + GetBodyTimeout(playlist);
			return GetSegmentDuration(playlist) + GetPollInterval(playlist) + attempt * 2 + RetryDelay;
		}
	}

	public override long MissedSegments => Interlocked.Read(ref _missedSegments);

	/// <param name="requestTimeout">Bounds every later playlist refresh as a whole and every segment download until its response headers arrive, because the stream client itself never times out; a segment body then gets at least two target durations.</param>
	public static async Task<LiveStreamSession> OpenAsync(HttpClient httpClient, Uri uri, TimeSpan requestTimeout, TimeProvider timeProvider, CancellationToken cancellationToken)
	{
		PlaylistSnapshot snapshot = await FetchPlaylistAsync(httpClient, uri, cancellationToken).ConfigureAwait(false);
		FirstSegment firstSegment = await PrepareFirstSegmentAsync(httpClient, snapshot, cancellationToken).ConfigureAwait(false);
		return new HlsStreamSession(httpClient, uri, requestTimeout, snapshot, firstSegment, timeProvider);
	}

	public override Task<StreamEndReason> CopyToAsync(Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
	{
		return CopyCoreAsync(destination, null, progress, cancellationToken);
	}

	public override Task<StreamEndReason> CopyToAsync(Stream destination, IHlsSegmentObserver segments, IProgress<long>? progress, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(segments);

		return CopyCoreAsync(destination, segments, progress, cancellationToken);
	}

	public override ValueTask DisposeAsync()
	{
		return ValueTask.CompletedTask;
	}

	private static TimeSpan GetPollInterval(HlsPlaylist playlist)
	{
		return GetSegmentDuration(playlist) / 2;
	}

	private static TimeSpan GetSegmentDuration(HlsPlaylist playlist)
	{
		return playlist.TargetDuration is { } duration && duration > TimeSpan.Zero ? duration : DefaultPollInterval * 2;
	}

	// A slow body is not a hung request, and a segment takes about a target duration to stream at its bit rate
	private TimeSpan GetBodyTimeout(HlsPlaylist playlist)
	{
		TimeSpan streaming = GetSegmentDuration(playlist) * 2;
		return streaming > _requestTimeout ? streaming : _requestTimeout;
	}

	// The refresh and prefetch stages never fail on their own: whatever ends them reaches the writer in order through the download queue, so nothing is left unobserved
	private async Task<StreamEndReason> CopyCoreAsync(Stream destination, IHlsSegmentObserver? segments, IProgress<long>? progress, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(destination);

		(HlsSegment first, byte[]? initData, byte[] firstData) = _firstSegment;
		using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		// A refresh must never wait for the downloads, or a short window could slide past segments never listed
		Channel<PendingSegment> listed = Channel.CreateUnbounded<PendingSegment>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
		// The one being written is already out of the channel, so its capacity is one less than the downloads allowed
		Channel<Task<Fetched>> fetched = Channel.CreateBounded<Task<Fetched>>(new BoundedChannelOptions(MaxConcurrentDownloads - 1) { SingleReader = true, SingleWriter = true });
		Task<Fetched> refreshing = RefreshAsync(first, listed.Writer, stop.Token);
		Task prefetching = PrefetchAsync(listed.Reader, refreshing, fetched.Writer, stop.Token);

		try
		{
			// Both were already downloaded and validated while opening, and every refresh repeats the init segment that must only be written once
			if (initData is not null)
			{
				segments?.OnInitSegment(initData);
				await WriteAsync(initData).ConfigureAwait(false);
			}

			await WriteSegmentAsync(firstData).ConfigureAwait(false);

			await foreach (Task<Fetched> fetching in fetched.Reader.ReadAllAsync(stop.Token).ConfigureAwait(false))
			{
				switch (await fetching.ConfigureAwait(false))
				{
					case Fetched.Segment { Data: var data }:
					{
						using (data)
						{
							await WriteSegmentAsync(data.Memory).ConfigureAwait(false);
						}

						break;
					}
					case Fetched.Ended { Reason: var reason }:
					{
						return reason;
					}
					case Fetched.Failed { Error: var error }:
					{
						error.Throw();
						break;
					}
					default:
					{
						throw new UnreachableException();
					}
				}
			}

			// The queue only ends early when the prefetch stage was cancelled
			cancellationToken.ThrowIfCancellationRequested();
			throw new UnreachableException("The segment queue ended without a reason.");
		}
		finally
		{
			// Whichever stage ended the copy, no download may keep running into a pooled buffer after it returns
			await stop.CancelAsync().ConfigureAwait(false);
			await prefetching.ConfigureAwait(false);
			await refreshing.ConfigureAwait(false);

			while (fetched.Reader.TryRead(out Task<Fetched>? left))
			{
				if (await left.ConfigureAwait(false) is Fetched.Segment { Data: var data })
				{
					data.Dispose();
				}
			}
		}

		async Task WriteSegmentAsync(ReadOnlyMemory<byte> data)
		{
			segments?.OnMediaSegment(data.Span);
			await WriteAsync(data).ConfigureAwait(false);
		}

		async Task WriteAsync(ReadOnlyMemory<byte> data)
		{
			await destination.WriteAsync(data, stop.Token).ConfigureAwait(false);
			progress?.Report(data.Length);
		}
	}

	private async Task<Fetched> RefreshAsync(HlsSegment first, ChannelWriter<PendingSegment> listed, CancellationToken cancellationToken)
	{
		try
		{
			return new Fetched.Ended(await ListAsync(first, listed, cancellationToken).ConfigureAwait(false));
		}
		catch (Exception exception)
		{
			return new Fetched.Failed(ExceptionDispatchInfo.Capture(exception));
		}
		finally
		{
			listed.Complete();
		}
	}

	private async Task<StreamEndReason> ListAsync(HlsSegment first, ChannelWriter<PendingSegment> listed, CancellationToken cancellationToken)
	{
		PlaylistSnapshot snapshot = _initialPlaylist;
		HlsSegmentTracker tracker = new(SegmentHistoryCapacity);

		// Older segments belong to a moov or timeline this output will never contain
		foreach (HlsSegment stale in snapshot.Playlist.Segments.Where(s => !IsSameEra(s, first)))
		{
			tracker.Skip(stale);
		}

		tracker.Add(first);

		while (true)
		{
			HlsPlaylist playlist = snapshot.Playlist;
			IReadOnlyList<HlsSegment> segments = playlist.Segments;
			int lastSeen = -1;

			for (int i = 0; i < segments.Count; ++i)
			{
				if (!tracker.HasSeen(segments[i]))
				{
					continue;
				}

				lastSeen = i;

				if (IsSameEra(segments[i], first))
				{
					tracker.Advance(segments[i]);
				}
			}

			// A window that starts in another era may follow a restart that renumbered the segments
			if (segments is [var head, ..] && IsSameEra(head, first))
			{
				Interlocked.Add(ref _missedSegments, tracker.CountMissed(playlist));
			}

			TimeSpan bodyTimeout = GetBodyTimeout(playlist);

			for (int i = 0; i < segments.Count; ++i)
			{
				HlsSegment segment = segments[i];

				if (tracker.HasSeen(segment))
				{
					continue;
				}

				// 播放列表按顺序列出分片，排在见过的分片之前的一定更早，来自窗口落后的节点；没有序号或属于旧时代的分片只能靠位置认出
				if (i < lastSeen)
				{
					tracker.Skip(segment);
					continue;
				}

				// An encoder restart or resolution change cannot continue the current file; its era is checked before the sequence number, which a restart may reset
				if (!IsSameEra(segment, first))
				{
					return StreamEndReason.Discontinuity;
				}

				if (!tracker.IsAhead(segment))
				{
					continue;
				}

				tracker.Add(segment);

				if (!listed.TryWrite(new PendingSegment(segment, new Uri(snapshot.BaseUri, segment.Uri), bodyTimeout)))
				{
					throw new UnreachableException("Only this stage completes the queue of listed segments.");
				}
			}

			if (playlist.EndList)
			{
				return StreamEndReason.EndOfStream;
			}

			// The interval follows the target duration of each refresh, which a periodic timer could not
			await Task.Delay(GetPollInterval(playlist), _timeProvider, cancellationToken).ConfigureAwait(false);
			Uri playlistUri = snapshot.Uri;
			snapshot = await RetryAsync((_, ct) => FetchPlaylistAsync(_httpClient, playlistUri, ct), cancellationToken).ConfigureAwait(false);
		}
	}

	// Waiting for room before starting a download is what limits the downloads in flight; the refresh outcome goes last, after every segment it listed
	private async Task PrefetchAsync(ChannelReader<PendingSegment> listed, Task<Fetched> refreshing, ChannelWriter<Task<Fetched>> fetched, CancellationToken cancellationToken)
	{
		try
		{
			await foreach (PendingSegment segment in listed.ReadAllAsync(cancellationToken).ConfigureAwait(false))
			{
				await EnqueueAsync(() => FetchAsync(segment, cancellationToken)).ConfigureAwait(false);
			}

			await EnqueueAsync(() => refreshing).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// The writer stops on the same token
		}
		finally
		{
			fetched.Complete();
		}

		async Task EnqueueAsync(Func<Task<Fetched>> start)
		{
			if (!await fetched.WaitToWriteAsync(cancellationToken).ConfigureAwait(false) || !fetched.TryWrite(start()))
			{
				throw new UnreachableException("Only this stage writes to or completes the download queue.");
			}
		}
	}

	private async Task<Fetched> FetchAsync(PendingSegment segment, CancellationToken cancellationToken)
	{
		// Each attempt buffers the whole segment, so a retry never writes a partial copy twice
		try
		{
			return new Fetched.Segment(await RetryAsync((timeout, ct) => DownloadAsync(segment, timeout, ct), cancellationToken).ConfigureAwait(false));
		}
		catch (Exception exception)
		{
			return new Fetched.Failed(ExceptionDispatchInfo.Capture(exception));
		}
	}

	// Only segments sharing an init segment and a timeline can go into the same file
	private static bool IsSameEra(HlsSegment segment, HlsSegment other)
	{
		return segment.MapUri == other.MapUri && segment.DiscontinuitySequence == other.DiscontinuitySequence;
	}

	private static async Task<PlaylistSnapshot> FetchPlaylistAsync(HttpClient httpClient, Uri uri, CancellationToken cancellationToken)
	{
		(Uri baseUri, string content) = await GetPlaylistAsync(httpClient, uri, cancellationToken).ConfigureAwait(false);

		if (HlsPlaylist.GetVariant(content) is { } variant)
		{
			uri = new Uri(baseUri, variant);
			(baseUri, content) = await GetPlaylistAsync(httpClient, uri, cancellationToken).ConfigureAwait(false);
		}

		return new PlaylistSnapshot(uri, baseUri, HlsPlaylist.Parse(content));
	}

	private static async Task<(Uri BaseUri, string Content)> GetPlaylistAsync(HttpClient httpClient, Uri uri, CancellationToken cancellationToken)
	{
		using HttpResponseMessage response = await httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
		response.EnsureSuccessStatusCode();

		// Segments are relative to the playlist after redirects, which may point to another CDN node
		return (response.RequestMessage?.RequestUri ?? uri, await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
	}

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
			initData = await httpClient.GetByteArrayAsync(new Uri(snapshot.BaseUri, mapUri), cancellationToken).ConfigureAwait(false);

			if (!MediaProbe.IsIsoBmff(initData, true))
			{
				throw new InvalidDataException($"The init segment {Describe(mapUri)} is not valid fMP4.");
			}
		}

		byte[] data = await httpClient.GetByteArrayAsync(new Uri(snapshot.BaseUri, first.Uri), cancellationToken).ConfigureAwait(false);
		EnsureMedia(first, data);

		return new FirstSegment(first, initData, data);
	}

	private static void EnsureMedia(HlsSegment segment, ReadOnlySpan<byte> data)
	{
		if (segment.MapUri is null ? !MediaProbe.IsTransportStream(data) : !MediaProbe.IsIsoBmff(data, false))
		{
			throw new InvalidDataException($"The segment {Describe(segment.Uri)} is not valid {(segment.MapUri is null ? "MPEG-TS" : "fMP4")}.");
		}
	}

	// Messages end up in logs, and a playlist entry may carry a signed query
	private static string Describe(string uri)
	{
		int end = uri.AsSpan().IndexOfAny('?', '#');
		return end < 0 ? uri : uri.Substring(0, end);
	}

	private static int GetInitialCapacity(long? contentLength)
	{
		return contentLength is long length and > 0 ? (int)Math.Min(length, MaxInitialSegmentCapacity) : DefaultSegmentCapacity;
	}

	private async Task<SegmentBuffer> DownloadAsync(PendingSegment segment, CancellationTokenSource timeout, CancellationToken cancellationToken)
	{
		using HttpResponseMessage response = await _httpClient.GetAsync(segment.Uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
		response.EnsureSuccessStatusCode();
		timeout.CancelAfter(segment.BodyTimeout);

		long? contentLength = response.Content.Headers.ContentLength;
		SegmentBuffer buffer = new(GetInitialCapacity(contentLength), contentLength);

		try
		{
			Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
			await buffer.ReadFromAsync(body, cancellationToken).ConfigureAwait(false);
			EnsureMedia(segment.Entry, buffer.Memory.Span);
			return buffer;
		}
		catch
		{
			buffer.Dispose();
			throw;
		}
	}

	private async Task<T> RetryAsync<T>(Func<CancellationTokenSource, CancellationToken, Task<T>> action, CancellationToken cancellationToken)
	{
		for (int attempt = 1; ; ++attempt)
		{
			try
			{
				return await WithTimeoutAsync(action, cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex) when (attempt < MaxAttempts && IsTransient(ex) && !cancellationToken.IsCancellationRequested)
			{
				await Task.Delay(RetryDelay, _timeProvider, cancellationToken).ConfigureAwait(false);
			}
		}

		// A CDN may answer a refresh with an error page and a 200 status, which only fails to parse
		static bool IsTransient(Exception ex)
		{
			return ex is HttpRequestException or IOException or InvalidDataException or FormatException or TimeoutException or TaskCanceledException { InnerException: TimeoutException };
		}
	}

	// The action may move the deadline once the response headers are in
	private async Task<T> WithTimeoutAsync<T>(Func<CancellationTokenSource, CancellationToken, Task<T>> action, CancellationToken cancellationToken)
	{
		using CancellationTokenSource timeout = new(_requestTimeout, _timeProvider);
		using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

		try
		{
			return await action(timeout, linked.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException exception) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
		{
			throw new TimeoutException("The request did not complete in time.", exception);
		}
	}

	private sealed record PlaylistSnapshot(Uri Uri, Uri BaseUri, HlsPlaylist Playlist);

	private sealed record FirstSegment(HlsSegment Segment, byte[]? InitData, byte[] Data);

	private sealed record PendingSegment(HlsSegment Entry, Uri Uri, TimeSpan BodyTimeout);

	private closed record Fetched
	{
		public sealed record Segment(SegmentBuffer Data) : Fetched;

		public sealed record Ended(StreamEndReason Reason) : Fetched;

		public sealed record Failed(ExceptionDispatchInfo Error) : Fetched;
	}
}
