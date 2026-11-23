using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;

namespace BilibiliLive.Streams;

internal sealed class HlsStreamSession : LiveStreamSession
{
	internal const int MaxAttempts = 3;

	// 足以在慢分片之后追上进度，又不至于压垮本就吃力的节点
	internal const int MaxConcurrentDownloads = 3;

	internal static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);
	internal static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

	private const int SegmentHistoryCapacity = 1024;
	private const int DefaultSegmentCapacity = 1 << 20;
	private const int MaxInitialSegmentCapacity = 64 << 20;

	private static readonly TimeSpan MinTargetDuration = TimeSpan.FromSeconds(1);

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

	// 列表间隔（一个目标时长加一个轮询间隔）再加一次完整的故障：失败的一次尝试、重试间隔和同样长的重试，因为空闲看门狗只是会话自身超时的后备
	// 一次尝试至少有两个请求超时那么长，所以刷新出一次故障也盖得住
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

	/// <param name="requestTimeout">限制之后每次刷新播放列表的全程，以及每次下载分片直到收到响应头的时长，因为流客户端自身从不超时</param>
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

	// HLS 的目标时长是整秒数，不足一秒的值只会来自不合规的播放列表，照用会让刷新几乎不停；夹到一秒而不是回退默认值，声明的分片越短刷新才不会反而越慢
	private static TimeSpan GetSegmentDuration(HlsPlaylist playlist)
	{
		return playlist.TargetDuration switch
		{
			{ Ticks: > 0 } duration => duration < MinTargetDuration ? MinTargetDuration : duration,
			_ => DefaultPollInterval * 2
		};
	}

	// 正文慢不等于请求挂起，按码率传完一个分片大约就要一个目标时长
	private TimeSpan GetBodyTimeout(HlsPlaylist playlist)
	{
		TimeSpan streaming = GetSegmentDuration(playlist) * 2;
		return streaming > _requestTimeout ? streaming : _requestTimeout;
	}

	// 刷新和预取阶段自己从不失败：结束它们的无论是什么，都经下载队列按顺序交给写入端，不会留下未观察的异常
	private async Task<StreamEndReason> CopyCoreAsync(Stream destination, IHlsSegmentObserver? segments, IProgress<long>? progress, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(destination);

		(HlsSegment first, byte[]? initData, byte[] firstData) = _firstSegment;
		using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		// 刷新绝不能等下载，否则窗口短时，分片还没被列出就可能滑出去
		Channel<PendingSegment> listed = Channel.CreateUnbounded<PendingSegment>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
		// 正在写的那个已经出了通道，所以容量比允许的下载数少一
		Channel<Task<Fetched>> fetched = Channel.CreateBounded<Task<Fetched>>(new BoundedChannelOptions(MaxConcurrentDownloads - 1) { SingleReader = true, SingleWriter = true });
		Task<Fetched> refreshing = RefreshAsync(first, listed.Writer, stop.Token);
		Task prefetching = PrefetchAsync(listed.Reader, refreshing, fetched.Writer, stop.Token);

		try
		{
			// 两者打开时已经下载并校验过；初始化段只能写一次，每次刷新却都会重复列出它
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

			// 只有预取阶段被取消时队列才会提前结束
			cancellationToken.ThrowIfCancellationRequested();
			throw new UnreachableException("The segment queue ended without a reason.");
		}
		finally
		{
			// 无论哪个阶段结束了复制，返回之后都不能再有下载写进池化的缓冲
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
			// ReSharper disable once AccessToDisposedClosure
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

		// 更早的分片属于这次输出永远不会包含的 moov 或时间线
		foreach (HlsSegment stale in snapshot.Playlist.Segments.Where(s => !s.IsSameEra(first)))
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

				if (segments[i].IsSameEra(first))
				{
					tracker.Advance(segments[i]);
				}
			}

			// 从其他时代开始的窗口可能出现在重启之后，重启可能重排了编号
			if (segments is [var head, ..] && head.IsSameEra(first))
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

				// 编码器重启或分辨率变化后不能接着写当前文件；先查时代再查序号，因为重启可能把序号清零
				if (!segment.IsSameEra(first))
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

			// 间隔跟随每次刷新的目标时长，周期计时器做不到
			await Task.Delay(GetPollInterval(playlist), _timeProvider, cancellationToken).ConfigureAwait(false);
			Uri playlistUri = snapshot.Uri;
			snapshot = await RetryAsync((_, ct) => FetchPlaylistAsync(_httpClient, playlistUri, ct), cancellationToken).ConfigureAwait(false);
		}
	}

	// 先等到有空位再开始下载，正是这一步限制了同时进行的下载数；刷新的结局排在它列出的所有分片之后
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
			// 写入端在同一个令牌上停止
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
		// 每次尝试都把整个分片缓冲下来，重试就不会把残缺的内容写两遍
		try
		{
			return new Fetched.Segment(await RetryAsync((timeout, ct) => DownloadAsync(segment, timeout, ct), cancellationToken).ConfigureAwait(false));
		}
		catch (Exception exception)
		{
			return new Fetched.Failed(ExceptionDispatchInfo.Capture(exception));
		}
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

		// 分片地址相对于重定向之后的播放列表，重定向可能指向另一个 CDN 节点
		return (response.RequestMessage?.RequestUri ?? uri, await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
	}

	private static async Task<FirstSegment> PrepareFirstSegmentAsync(HttpClient httpClient, PlaylistSnapshot snapshot, CancellationToken cancellationToken)
	{
		if (snapshot.Playlist.Segments is not [.., var latest])
		{
			throw new InvalidDataException("The playlist lists no segment.");
		}

		// 从最新时代的开头录起，录得最多，又不会把不同时代混进一个文件
		HlsSegment first = snapshot.Playlist.Segments.First(s => s.IsSameEra(latest));
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
		(bool valid, string format) = segment.MapUri is null ? (MediaProbe.IsTransportStream(data), "MPEG-TS") : (MediaProbe.IsIsoBmff(data, false), "fMP4");

		if (!valid)
		{
			throw new InvalidDataException($"The segment {Describe(segment.Uri)} is not valid {format}.");
		}
	}

	// 消息会写进日志，而播放列表条目可能带有签名的查询参数
	private static string Describe(string uri)
	{
		int end = uri.AsSpan().IndexOfAny('?', '#');
		return end < 0 ? uri : uri.Substring(0, end);
	}

	private static int GetInitialCapacity(long? contentLength)
	{
		return contentLength is { } length and > 0 ? (int)Math.Min(length, MaxInitialSegmentCapacity) : DefaultSegmentCapacity;
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

		// CDN 可能以 200 状态用错误页面回应刷新，这种情况只表现为解析失败
		static bool IsTransient(Exception ex)
		{
			return ex is HttpRequestException or IOException or InvalidDataException or FormatException or TimeoutException or TaskCanceledException { InnerException: TimeoutException };
		}
	}

	// 收到响应头之后，action 可以推迟截止时间
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
