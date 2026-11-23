using BilibiliLive.Streams;
using BilibiliLive.Tests.Fakes;
using System.Diagnostics;
using System.Net;
using TUnit.Assertions.Enums;

namespace BilibiliLive.Tests.Streams;

public class HlsStreamSessionTest
{
	private const string Head = "#EXTM3U\n#EXT-X-TARGETDURATION:2\n";
	private const string EndList = "#EXT-X-ENDLIST\n";

	private static readonly Uri PlaylistUri = new("https://cdn.test/live-bvc/1/live_2500/index.m3u8?expires=1&sign=x");
	private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

	private static HttpResponseMessage Segment(HttpRequestMessage request)
	{
		string path = request.RequestUri?.AbsolutePath ?? string.Empty;
		return Responses.Bytes(MediaSamples.ForPath(path, Path.GetFileNameWithoutExtension(path)));
	}

	// 播放列表按顺序给出，用完后一直给最后一个；其余请求都按分片回应
	private static FakeHttpMessageHandler Serve(params string[] playlists)
	{
		int served = 0;
		return new FakeHttpMessageHandler(request => request.RequestUri == PlaylistUri
			? Responses.Text(playlists[Math.Min(Interlocked.Increment(ref served), playlists.Length) - 1])
			: Segment(request));
	}

	// Polls and retry delays pass at once, a request only times out when a test advances the clock
	private static ShortDelayTimeProvider NewTime()
	{
		return new ShortDelayTimeProvider(RequestTimeout);
	}

	private static async Task<CopyResult> CopyAsync(HttpMessageHandler handler, ShortDelayTimeProvider time, CancellationToken cancellationToken)
	{
		using HttpClient client = new(handler);
		await using LiveStreamSession session = await HlsStreamSession.OpenAsync(client, PlaylistUri, RequestTimeout, time, cancellationToken);
		using MemoryStream output = new();
		RecordingProgress progress = new();

		StreamEndReason reason = await session.CopyToAsync(output, progress, cancellationToken);

		return new CopyResult(MediaSamples.Markers(output.ToArray()), progress.Reports, reason, session.MissedSegments);
	}

	[Test]
	[Timeout(10_000)]
	public async Task WritesMapOnceDeduplicatesSegmentsAndStopsAtEndListAsync(CancellationToken cancellationToken)
	{
		const string mapped = "#EXTM3U\n#EXT-X-VERSION:7\n#EXT-X-TARGETDURATION:2\n#EXT-X-MAP:URI=\"h1.m4s\"\n";
		FakeHttpMessageHandler handler = Serve
		(
			mapped + "#EXTINF:1.00,\n1.m4s\n#EXTINF:1.00,\n2.m4s\n",
			mapped + "#EXTINF:1.00,\n2.m4s\n#EXTINF:1.00,\n3.m4s\n",
			mapped + "#EXTINF:1.00,\n3.m4s\n#EXTINF:1.00,\n4.m4s\n" + EndList
		);
		ShortDelayTimeProvider time = NewTime();

		CopyResult copy = await CopyAsync(handler, time, cancellationToken);

		long init = MediaSamples.Fmp4Init("h1").Length;
		long fragment = MediaSamples.Fmp4Fragment("1").Length;

		using (Assert.Multiple())
		{
			await Assert.That(copy.Output).IsEqualTo("[h1][1][2][3][4]");
			await Assert.That(copy.Progress).IsEquivalentTo([init, fragment, fragment, fragment, fragment], CollectionOrdering.Matching);
			await Assert.That(handler.Requests.Select(static r => Path.GetFileName(r.Uri.AbsolutePath)))
				.IsEquivalentTo(["index.m3u8", "index.m3u8", "index.m3u8", "h1.m4s", "1.m4s", "2.m4s", "3.m4s", "4.m4s"])
				.Because("打开时下载并校验过的初始化段和第一段不再下载，重复列出的分片只下载一次");
			await Assert.That(time.Delays).IsEquivalentTo([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)], CollectionOrdering.Matching);
		}
	}

	[Test]
	[Timeout(10_000)]
	[Arguments("#EXTM3U\n#EXT-X-MAP:URI=\"h1.m4s\"\n1.m4s\n2.m4s\n" + EndList, new[] { "init:[h1]", "media:[1]", "media:[2]" })]
	[Arguments("#EXTM3U\n1.ts\n2.ts\n" + EndList, new[] { "media:[1]", "media:[2]" })]
	public async Task ReportsEachSegmentRightBeforeWritingExactlyItsBytesAsync(string playlist, string[] expected, CancellationToken cancellationToken)
	{
		using HttpClient client = new(Serve(playlist));
		await using LiveStreamSession session = await HlsStreamSession.OpenAsync(client, PlaylistUri, RequestTimeout, NewTime(), cancellationToken);
		using MemoryStream output = new();
		SegmentLog segments = new(output);

		await session.CopyToAsync(output, segments, null, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(segments.Entries).IsEquivalentTo(expected, CollectionOrdering.Matching);
			await Assert.That(segments.MatchedEveryWrite()).IsTrue();
			await Assert.That(segments.Length).IsEqualTo(output.Length);
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task FollowsAMasterPlaylistAndRefreshesItsVariantAsync(CancellationToken cancellationToken)
	{
		const string variantPath = "/edge/live-bvc/1/live_2500/index.m3u8";
		string[] playlists =
		[
			"#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXT-X-MAP:URI=\"h1.m4s\"\n1.m4s\n2.m4s\n",
			"#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXT-X-MAP:URI=\"h1.m4s\"\n2.m4s\n3.m4s\n" + EndList
		];
		int playlistIndex = 0;
		FakeHttpMessageHandler handler = new(request => request.RequestUri?.AbsolutePath switch
		{
			"/live-bvc/1/live_2500/index.m3u8" => Responses.Text($"#EXTM3U\n#EXT-X-VERSION:7\n#EXT-X-STREAM-INF:PROGRAM-ID=1,BANDWIDTH=2560000\nhttps://edge.test{variantPath}?sign=y\n"),
			variantPath => Responses.Text(playlists[Math.Min(playlistIndex++, playlists.Length - 1)]),
			{ } path when path.StartsWith("/edge/live-bvc/1/live_2500/", StringComparison.Ordinal) => Segment(request),
			_ => Responses.NotFound()
		});

		CopyResult copy = await CopyAsync(handler, NewTime(), cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That((copy.Output, copy.Reason)).IsEqualTo(("[h1][1][2][3]", StreamEndReason.EndOfStream));
			await Assert.That(handler.RequestsTo(PlaylistUri.AbsolutePath)).Count().IsEqualTo(1);
			await Assert.That(handler.RequestsTo(variantPath)).Count().IsEqualTo(2);
		}
	}

	[Test]
	[Arguments(Head)]
	[Arguments(Head + EndList)]
	public async Task PlaylistWithoutSegmentsIsRejectedAsync(string playlist, CancellationToken cancellationToken)
	{
		using HttpClient client = new(Serve(playlist));

		await Assert.ThrowsAsync<InvalidDataException>(() => HlsStreamSession.OpenAsync(client, PlaylistUri, RequestTimeout, NewTime(), cancellationToken));
	}

	[Test]
	[Arguments("#EXTM3U\n#EXTINF:1,\nbad.ts?sign=secret\n", "bad.ts")]
	[Arguments("#EXTM3U\n#EXT-X-MAP:URI=\"h1.m4s\"\n#EXTINF:1,\nbad.m4s?sign=secret\n", "bad.m4s")]
	[Arguments("#EXTM3U\n#EXT-X-MAP:URI=\"bad.mp4?sign=secret#x\"\n#EXTINF:1,\n1.m4s\n", "bad.mp4")]
	public async Task InvalidFirstSegmentOrMapIsRejectedWithoutItsQueryAsync(string playlist, string invalid, CancellationToken cancellationToken)
	{
		using HttpClient client = new(new FakeHttpMessageHandler(request => request.RequestUri switch
		{
			{ } uri when uri == PlaylistUri => Responses.Text(playlist),
			{ } uri when uri.AbsolutePath.EndsWith(invalid, StringComparison.Ordinal) => Responses.Text("<html>blocked</html>"),
			_ => Segment(request)
		}));

		await Assert.ThrowsAsync<InvalidDataException>(() => HlsStreamSession.OpenAsync(client, PlaylistUri, RequestTimeout, NewTime(), cancellationToken))
			.WithMessageContaining(invalid)
			.And.WithMessageNotContaining("sign");
	}

	// 后三行的新时代把编号从头排起，比已经写出的分片还小
	[Test]
	[Timeout(10_000)]
	[Arguments("#EXT-X-MAP:URI=\"h1.m4s\"\n1.m4s\n2.m4s\n", "#EXT-X-MAP:URI=\"h1.m4s\"\n2.m4s\n#EXT-X-MAP:URI=\"h2.m4s\"\n3.m4s\n", "[h1][1][2]", "3.m4s")]
	[Arguments("a.ts\nb.ts\n", "b.ts\n#EXT-X-DISCONTINUITY\nc.ts\n", "[a][b]", "c.ts")]
	[Arguments("#EXT-X-MEDIA-SEQUENCE:10\n10.ts\n11.ts\n", "#EXT-X-DISCONTINUITY-SEQUENCE:1\n#EXT-X-MEDIA-SEQUENCE:0\n0.ts\n", "[10][11]", "0.ts")]
	[Arguments("#EXT-X-MEDIA-SEQUENCE:10\n10.ts\n11.ts\n", "#EXT-X-MEDIA-SEQUENCE:0\n#EXT-X-DISCONTINUITY\n0.ts\n", "[10][11]", "0.ts")]
	[Arguments("#EXT-X-MAP:URI=\"h1.m4s\"\n#EXT-X-MEDIA-SEQUENCE:10\n10.m4s\n11.m4s\n", "#EXT-X-MAP:URI=\"h2.m4s\"\n#EXT-X-MEDIA-SEQUENCE:0\n0.m4s\n", "[h1][10][11]", "0.m4s")]
	public async Task NewEraEndsTheCopyBeforeItsFirstSegmentAsync(string opened, string refreshed, string expected, string newSegment, CancellationToken cancellationToken)
	{
		// 最后一次刷新结束列表，没认出新时代时复制也不会一直刷新下去
		FakeHttpMessageHandler handler = Serve(Head + opened, Head + refreshed, Head + refreshed, Head + refreshed + EndList);

		CopyResult copy = await CopyAsync(handler, NewTime(), cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That((copy.Output, copy.Reason)).IsEqualTo((expected, StreamEndReason.Discontinuity));
			await Assert.That(handler.RequestsTo(PlaylistUri.AbsolutePath)).Count().IsEqualTo(2);
			await Assert.That(handler.RequestsTo($"/live-bvc/1/live_2500/{newSegment}")).IsEmpty();
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task NewSessionStartsAtTheLatestTimelineAndFollowsTheSlidingWindowAsync(CancellationToken cancellationToken)
	{
		FakeHttpMessageHandler handler = Serve("#EXTM3U\n#EXT-X-DISCONTINUITY-SEQUENCE:0\na.ts\n#EXT-X-DISCONTINUITY\nb.ts\n", "#EXTM3U\n#EXT-X-DISCONTINUITY-SEQUENCE:1\nb.ts\nc.ts\n" + EndList);

		CopyResult copy = await CopyAsync(handler, NewTime(), cancellationToken);

		await Assert.That((copy.Output, copy.Reason)).IsEqualTo(("[b][c]", StreamEndReason.EndOfStream));
	}

	// 刷新列出的旧时代分片排在见过的分片前面：前两行是打开时就列出的，后三行来自窗口落后的节点；最后一行的落后窗口里只剩打开时跳过的旧时代分片
	[Test]
	[Timeout(10_000)]
	[Arguments("a.ts\n#EXT-X-DISCONTINUITY\nb.ts\n", "a.ts\n#EXT-X-DISCONTINUITY\nb.ts\nc.ts\n", "[b][c]", "a.ts")]
	[Arguments("#EXT-X-MEDIA-SEQUENCE:1\n#EXT-X-MAP:URI=\"h1.m4s\"\n1.m4s\n#EXT-X-MAP:URI=\"h2.m4s\"\n2.m4s\n", "#EXT-X-MEDIA-SEQUENCE:1\n#EXT-X-MAP:URI=\"h1.m4s\"\n1.m4s\n#EXT-X-MAP:URI=\"h2.m4s\"\n2.m4s\n3.m4s\n", "[h2][2][3]", "1.m4s")]
	[Arguments("#EXT-X-MEDIA-SEQUENCE:7\n#EXT-X-MAP:URI=\"h2.m4s\"\n7.m4s\n8.m4s\n", "#EXT-X-MEDIA-SEQUENCE:6\n#EXT-X-MAP:URI=\"h1.m4s\"\n6.m4s\n#EXT-X-MAP:URI=\"h2.m4s\"\n7.m4s\n8.m4s\n9.m4s\n", "[h2][7][8][9]", "6.m4s")]
	[Arguments("#EXT-X-DISCONTINUITY-SEQUENCE:1\n#EXT-X-MEDIA-SEQUENCE:7\n7.ts\n8.ts\n", "#EXT-X-MEDIA-SEQUENCE:6\n6.ts\n#EXT-X-DISCONTINUITY\n7.ts\n8.ts\n9.ts\n", "[7][8][9]", "6.ts")]
	[Arguments("#EXT-X-MEDIA-SEQUENCE:8\n#EXT-X-MAP:URI=\"h1.m4s\"\n8.m4s\n9.m4s\n#EXT-X-MAP:URI=\"h2.m4s\"\n10.m4s\n", "#EXT-X-MEDIA-SEQUENCE:7\n#EXT-X-MAP:URI=\"h1.m4s\"\n7.m4s\n8.m4s\n9.m4s\n", "[h2][10]", "7.m4s")]
	public async Task OlderEraListedByARefreshIsNotANewTimelineAsync(string opened, string refreshed, string expected, string older, CancellationToken cancellationToken)
	{
		FakeHttpMessageHandler handler = Serve(Head + opened, Head + refreshed + EndList);

		CopyResult copy = await CopyAsync(handler, NewTime(), cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That((copy.Output, copy.Reason)).IsEqualTo((expected, StreamEndReason.EndOfStream));
			await Assert.That(handler.RequestsTo($"/live-bvc/1/live_2500/{older}")).IsEmpty();
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task HungSegmentRequestTimesOutAndIsRetriedAsync(CancellationToken cancellationToken)
	{
		int attempts = 0;
		TaskCompletionSource hung = new(TaskCreationOptions.RunContinuationsAsynchronously);
		FakeHttpMessageHandler handler = new(async (request, ct) =>
		{
			if (request.RequestUri == PlaylistUri)
			{
				return Responses.Text("#EXTM3U\na.ts\nb.ts\n" + EndList);
			}

			if (request.RequestUri?.AbsolutePath.EndsWith("b.ts", StringComparison.Ordinal) is true && Interlocked.Increment(ref attempts) is 1)
			{
				hung.SetResult();
				await Task.Delay(Timeout.Infinite, ct);
			}

			return Segment(request);
		});
		ShortDelayTimeProvider time = NewTime();

		Task<CopyResult> copying = CopyAsync(handler, time, cancellationToken);
		await hung.Task.WaitAsync(cancellationToken);
		time.Advance(RequestTimeout);
		CopyResult copy = await copying;

		await Assert.That((copy.Output, attempts)).IsEqualTo(("[a][b]", 2));
	}

	[Test]
	[Timeout(10_000)]
	public async Task HungRefreshTimesOutAndIsRetriedAsync(CancellationToken cancellationToken)
	{
		int refreshes = 0;
		TaskCompletionSource hung = new(TaskCreationOptions.RunContinuationsAsynchronously);
		FakeHttpMessageHandler handler = new(async (request, ct) =>
		{
			if (request.RequestUri != PlaylistUri)
			{
				return Segment(request);
			}

			switch (Interlocked.Increment(ref refreshes))
			{
				case 1:
				{
					return Responses.Text(Head + "a.ts\n");
				}
				case 2:
				{
					hung.SetResult();
					await Task.Delay(Timeout.Infinite, ct);
					throw new UnreachableException();
				}
				default:
				{
					return Responses.Text(Head + "a.ts\nb.ts\n" + EndList);
				}
			}
		});
		ShortDelayTimeProvider time = NewTime();

		Task<CopyResult> copying = CopyAsync(handler, time, cancellationToken);
		await hung.Task.WaitAsync(cancellationToken);
		time.Advance(RequestTimeout);
		CopyResult copy = await copying;

		await Assert.That((copy.Output, copy.Reason, refreshes)).IsEqualTo(("[a][b]", StreamEndReason.EndOfStream, 3));
	}

	[Test]
	[Timeout(10_000)]
	[Arguments(false)]
	[Arguments(true)]
	public async Task FailedRefreshIsRetriedAsync(bool errorPage, CancellationToken cancellationToken)
	{
		int refreshes = 0;
		FakeHttpMessageHandler handler = new(request => request.RequestUri == PlaylistUri
			? Interlocked.Increment(ref refreshes) switch
			{
				1 => Responses.Text(Head + "a.ts\n"),
				// An error page served with a 200 status fails to parse instead of failing the request
				2 => errorPage ? Responses.Text("<html>blocked</html>") : Responses.Status(HttpStatusCode.ServiceUnavailable),
				_ => Responses.Text(Head + "a.ts\nb.ts\n" + EndList)
			}
			: Segment(request));
		ShortDelayTimeProvider time = NewTime();

		CopyResult copy = await CopyAsync(handler, time, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That((copy.Output, copy.Reason, refreshes)).IsEqualTo(("[a][b]", StreamEndReason.EndOfStream, 3));
			await Assert.That(time.Delays).IsEquivalentTo([TimeSpan.FromSeconds(1), HlsStreamSession.RetryDelay], CollectionOrdering.Matching);
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task CancellingTheCopyIsNeitherATimeoutNorRetriedAsync(CancellationToken cancellationToken)
	{
		TaskCompletionSource hung = new(TaskCreationOptions.RunContinuationsAsynchronously);
		FakeHttpMessageHandler handler = new(async (request, ct) =>
		{
			if (request.RequestUri == PlaylistUri)
			{
				return Responses.Text("#EXTM3U\na.ts\nb.ts\n" + EndList);
			}

			if (request.RequestUri?.AbsolutePath.EndsWith("b.ts", StringComparison.Ordinal) is true)
			{
				hung.SetResult();
				await Task.Delay(Timeout.Infinite, ct);
			}

			return Segment(request);
		});
		using HttpClient client = new(handler);
		ShortDelayTimeProvider time = NewTime();
		await using LiveStreamSession session = await HlsStreamSession.OpenAsync(client, PlaylistUri, RequestTimeout, time, cancellationToken);
		using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		using MemoryStream output = new();

		Task<StreamEndReason> copying = session.CopyToAsync(output, null, stop.Token);
		await hung.Task.WaitAsync(cancellationToken);
		await stop.CancelAsync();
		await Assert.ThrowsAsync<OperationCanceledException>(() => copying);

		using (Assert.Multiple())
		{
			await Assert.That(handler.RequestsTo("/live-bvc/1/live_2500/b.ts")).Count().IsEqualTo(1);
			await Assert.That(time.Delays).IsEmpty();
		}
	}

	// A request that only gives up after its timeout already fired still belongs to a copy the caller cancelled
	[Test]
	[Timeout(10_000)]
	public async Task CancellingTheCopyPastTheRequestTimeoutIsNotATimeoutAsync(CancellationToken cancellationToken)
	{
		TaskCompletionSource hung = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
		FakeHttpMessageHandler handler = new(async (request, ct) =>
		{
			if (request.RequestUri == PlaylistUri)
			{
				return Responses.Text("#EXTM3U\na.ts\nb.ts\n" + EndList);
			}

			if (request.RequestUri?.AbsolutePath.EndsWith("b.ts", StringComparison.Ordinal) is true)
			{
				hung.SetResult();
				await released.Task.WaitAsync(cancellationToken);
				throw new OperationCanceledException(ct);
			}

			return Segment(request);
		});
		using HttpClient client = new(handler);
		ShortDelayTimeProvider time = NewTime();
		await using LiveStreamSession session = await HlsStreamSession.OpenAsync(client, PlaylistUri, RequestTimeout, time, cancellationToken);
		using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		using MemoryStream output = new();

		Task<StreamEndReason> copying = session.CopyToAsync(output, null, stop.Token);
		await hung.Task.WaitAsync(cancellationToken);
		time.Advance(RequestTimeout);
		await stop.CancelAsync();
		released.SetResult();
		await Assert.ThrowsAsync<OperationCanceledException>(() => copying);

		using (Assert.Multiple())
		{
			await Assert.That(handler.RequestsTo("/live-bvc/1/live_2500/b.ts")).Count().IsEqualTo(1);
			await Assert.That(time.Delays).IsEmpty();
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task CancellingAHungRefreshIsNeitherATimeoutNorRetriedAsync(CancellationToken cancellationToken)
	{
		int refreshes = 0;
		TaskCompletionSource hung = new(TaskCreationOptions.RunContinuationsAsynchronously);
		FakeHttpMessageHandler handler = new(async (request, ct) =>
		{
			if (request.RequestUri != PlaylistUri)
			{
				return Segment(request);
			}

			if (Interlocked.Increment(ref refreshes) > 1)
			{
				hung.TrySetResult();
				await Task.Delay(Timeout.Infinite, ct);
			}

			return Responses.Text(Head + "a.ts\n");
		});
		using HttpClient client = new(handler);
		ShortDelayTimeProvider time = NewTime();
		await using LiveStreamSession session = await HlsStreamSession.OpenAsync(client, PlaylistUri, RequestTimeout, time, cancellationToken);
		using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		using MemoryStream output = new();

		Task<StreamEndReason> copying = session.CopyToAsync(output, null, stop.Token);
		await hung.Task.WaitAsync(cancellationToken);
		await stop.CancelAsync();
		await Assert.ThrowsAsync<OperationCanceledException>(() => copying);

		using (Assert.Multiple())
		{
			await Assert.That(refreshes).IsEqualTo(2);
			await Assert.That(time.Delays).IsEquivalentTo([TimeSpan.FromSeconds(1)], CollectionOrdering.Matching);
			await Assert.That(MediaSamples.Markers(output.ToArray())).IsEqualTo("[a]");
		}
	}

	// The session lets a body stream for two target durations without retrying, so the watchdog must wait at least a listing gap plus such a body
	[Test]
	[Timeout(10_000)]
	public async Task MaxSilenceCoversABodyTheSessionStillWaitsForAsync(CancellationToken cancellationToken)
	{
		TimeSpan targetDuration = TimeSpan.FromSeconds(5);
		// 长于重试间隔：正文超时没有放宽时会立刻重试并多请求一次，而不是停在重试间隔上等到测试超时；又不能太长，否则不计正文超时的 MaxSilence 也盖得住这段正文
		TimeSpan requestTimeout = TimeSpan.FromMilliseconds(1500);
		TimeSpan body = targetDuration * 2 - TimeSpan.FromSeconds(1);
		TaskCompletionSource headersSent = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource bodyReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
		FakeHttpMessageHandler handler = new(request => request.RequestUri?.AbsolutePath switch
		{
			_ when request.RequestUri == PlaylistUri => Responses.Text("#EXTM3U\n#EXT-X-TARGETDURATION:5\na.ts\nb.ts\n" + EndList),
			{ } path when path.EndsWith("b.ts", StringComparison.Ordinal) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new GatedStream(MediaSamples.TransportStream("b"), headersSent, bodyReleased.Task)) },
			_ => Segment(request)
		});
		using HttpClient client = new(handler);
		ShortDelayTimeProvider time = new(requestTimeout);
		await using LiveStreamSession session = await HlsStreamSession.OpenAsync(client, PlaylistUri, requestTimeout, time, cancellationToken);
		using MemoryStream output = new();

		Task<StreamEndReason> copying = session.CopyToAsync(output, null, cancellationToken);
		await headersSent.Task.WaitAsync(cancellationToken);
		time.Advance(body);
		bodyReleased.SetResult();
		await copying;

		// A target duration until the next segment is listed, plus a poll interval
		TimeSpan listingGap = targetDuration + targetDuration / 2;

		using (Assert.Multiple())
		{
			await Assert.That(MediaSamples.Markers(output.ToArray())).IsEqualTo("[a][b]");
			await Assert.That(handler.RequestsTo("/live-bvc/1/live_2500/b.ts")).Count().IsEqualTo(1);
			await Assert.That(session.MaxSilence ?? TimeSpan.Zero).IsGreaterThanOrEqualTo(listingGap + body);
		}
	}

	[Test]
	[Arguments("#EXTM3U\n#EXT-X-TARGETDURATION:5\na.ts\n", 5, 38.5)]
	[Arguments("#EXTM3U\n#EXT-X-TARGETDURATION:5\na.ts\n", 1, 30.5)]
	[Arguments("#EXTM3U\n#EXT-X-TARGETDURATION:1\n#EXT-X-MAP:URI=\"h1.m4s\"\n1.m4s\n", 5, 22.5)]
	public async Task MaxSilenceCoversAGapAndOneFailedAttemptWithItsRetryAsync(string playlist, int requestTimeoutSeconds, double expectedSeconds, CancellationToken cancellationToken)
	{
		using HttpClient client = new(Serve(playlist));

		await using LiveStreamSession session = await HlsStreamSession.OpenAsync(client, PlaylistUri, TimeSpan.FromSeconds(requestTimeoutSeconds), NewTime(), cancellationToken);

		await Assert.That(session.MaxSilence).IsEqualTo(TimeSpan.FromSeconds(expectedSeconds));
	}

	// 200 的错误页面过不了媒体校验，其他状态码直接请求失败
	[Test]
	[Timeout(10_000)]
	[Arguments("#EXT-X-MAP:URI=\"h1.m4s\"\n1.m4s\nbad.m4s\n", HttpStatusCode.OK, typeof(InvalidDataException), "[h1][1]")]
	[Arguments("#EXTINF:1,\n1.ts\n#EXTINF:1,\nbad.ts\n", HttpStatusCode.ServiceUnavailable, typeof(HttpRequestException), "[1]")]
	public async Task SegmentFailingEveryAttemptThrowsAfterTheSegmentsBeforeItAsync(string listed, HttpStatusCode status, Type expected, string written, CancellationToken cancellationToken)
	{
		FakeHttpMessageHandler handler = new(request => request.RequestUri == PlaylistUri
			? Responses.Text("#EXTM3U\n" + listed + EndList)
			: Path.GetFileNameWithoutExtension(request.RequestUri?.AbsolutePath) is "bad"
				? new HttpResponseMessage(status) { Content = new StringContent("<html>blocked</html>") }
				: Segment(request));
		using HttpClient client = new(handler);
		await using LiveStreamSession session = await HlsStreamSession.OpenAsync(client, PlaylistUri, RequestTimeout, NewTime(), cancellationToken);
		using MemoryStream output = new();

		await Assert.ThrowsAsync(expected, () => session.CopyToAsync(output, null, cancellationToken));

		using (Assert.Multiple())
		{
			await Assert.That(MediaSamples.Markers(output.ToArray())).IsEqualTo(written);
			await Assert.That(handler.Requests.Where(static r => Path.GetFileNameWithoutExtension(r.Uri.AbsolutePath) is "bad")).Count().IsEqualTo(HlsStreamSession.MaxAttempts);
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task RefreshRedirectedToAnotherNodeDoesNotRewriteSegmentsAsync(CancellationToken cancellationToken)
	{
		(string Host, string Content)[] playlists =
		[
			("node-a.test", Head + "100.ts\n101.ts\n"),
			("node-b.test", Head + "101.ts\n102.ts\n" + EndList)
		];
		int playlistIndex = 0;
		FakeHttpMessageHandler handler = new(request =>
		{
			if (request.RequestUri == PlaylistUri)
			{
				(string host, string content) = playlists[Math.Min(playlistIndex++, playlists.Length - 1)];
				HttpResponseMessage response = Responses.Text(content);
				response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, new Uri($"https://{host}/live/index.m3u8"));
				return response;
			}

			return Segment(request);
		});

		CopyResult copy = await CopyAsync(handler, NewTime(), cancellationToken);

		await Assert.That(copy.Output).IsEqualTo("[100][101][102]");
	}

	// 更早的分片此前没有列出过：前两行比最后入队的分片小两个或一个；第三行的刷新不带序号，只能靠位置认出；最后一行落后窗口的开头低于前一次不带序号的刷新入队的分片
	[Test]
	[Timeout(10_000)]
	[Arguments(new[] { Head + "#EXT-X-MEDIA-SEQUENCE:5\n5.ts\n6.ts\n", Head + "#EXT-X-MEDIA-SEQUENCE:4\n4.ts\n5.ts\n6.ts\n7.ts\n" + EndList }, "[5][6][7]", "4.ts")]
	[Arguments(new[] { Head + "#EXT-X-MEDIA-SEQUENCE:5\n5.ts\n", Head + "#EXT-X-MEDIA-SEQUENCE:4\n4.ts\n5.ts\n6.ts\n7.ts\n" + EndList }, "[5][6][7]", "4.ts")]
	[Arguments(new[] { Head + "#EXT-X-MEDIA-SEQUENCE:5\n5.ts\n6.ts\n", Head + "4.ts\n5.ts\n6.ts\n7.ts\n" + EndList }, "[5][6][7]", "4.ts")]
	[Arguments(new[] { Head + "#EXT-X-MEDIA-SEQUENCE:1\n1.ts\n2.ts\n", Head + "8.ts\n9.ts\n", Head + "#EXT-X-MEDIA-SEQUENCE:6\n6.ts\n7.ts\n8.ts\n9.ts\n10.ts\n" + EndList }, "[1][2][8][9][10]", "6.ts")]
	public async Task RefreshFromALaggingNodeDoesNotAppendOlderSegmentsAsync(string[] playlists, string expected, string older, CancellationToken cancellationToken)
	{
		FakeHttpMessageHandler handler = Serve(playlists);

		CopyResult copy = await CopyAsync(handler, NewTime(), cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(copy.Output).IsEqualTo(expected);
			await Assert.That(handler.RequestsTo($"/live-bvc/1/live_2500/{older}")).IsEmpty();
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task RefreshThatOmitsTheMediaSequenceDoesNotRewriteSegmentsAsync(CancellationToken cancellationToken)
	{
		CopyResult copy = await CopyAsync(Serve(Head + "#EXT-X-MEDIA-SEQUENCE:1\n1.ts\n2.ts\n", Head + "1.ts\n2.ts\n3.ts\n" + EndList), NewTime(), cancellationToken);

		await Assert.That(copy.Output).IsEqualTo("[1][2][3]");
	}

	[Test]
	[Timeout(10_000)]
	public async Task SegmentsQueuedFromAnUnnumberedRefreshAreNotCountedAsMissedAsync(CancellationToken cancellationToken)
	{
		FakeHttpMessageHandler handler = Serve
		(
			Head + "#EXT-X-MEDIA-SEQUENCE:1\n1.ts\n2.ts\n",
			Head + "2.ts\n3.ts\n4.ts\n5.ts\n",
			Head + "#EXT-X-MEDIA-SEQUENCE:5\n5.ts\n6.ts\n7.ts\n" + EndList
		);

		CopyResult copy = await CopyAsync(handler, NewTime(), cancellationToken);

		await Assert.That((copy.Output, copy.MissedSegments)).IsEqualTo(("[1][2][3][4][5][6][7]", 0L));
	}

	[Test]
	[Timeout(10_000)]
	public async Task SegmentsResolveAgainstRedirectedPlaylistAsync(CancellationToken cancellationToken)
	{
		Uri redirected = new("https://edge.test/node/7/index.m3u8?token=y");
		FakeHttpMessageHandler handler = new(request =>
		{
			if (request.RequestUri == PlaylistUri)
			{
				HttpResponseMessage response = Responses.Text("#EXTM3U\n#EXTINF:1,\nseg-1.ts\n#EXTINF:1,\n../shared/seg-2.ts?part=2\n" + EndList);
				response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, redirected);
				return response;
			}

			return request.RequestUri?.Host is "edge.test" ? Responses.Bytes(MediaSamples.TransportStream(request.RequestUri.PathAndQuery)) : Responses.NotFound();
		});

		CopyResult copy = await CopyAsync(handler, NewTime(), cancellationToken);

		await Assert.That(copy.Output).IsEqualTo("[/node/7/seg-1.ts][/node/shared/seg-2.ts?part=2]");
	}

	[Test]
	[Timeout(10_000)]
	public async Task MissingTargetDurationPollsEverySecondAsync(CancellationToken cancellationToken)
	{
		ShortDelayTimeProvider time = NewTime();

		CopyResult copy = await CopyAsync(Serve("#EXTM3U\n#EXTINF:1,\na.ts\n", "#EXTM3U\n#EXTINF:1,\na.ts\n" + EndList), time, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(copy.Output).IsEqualTo("[a]");
			await Assert.That(time.Delays).IsEquivalentTo([HlsStreamSession.DefaultPollInterval], CollectionOrdering.Matching);
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task FailedSegmentIsRetriedAndWrittenOnceAsync(CancellationToken cancellationToken)
	{
		int attempts = 0;
		FakeHttpMessageHandler handler = new(request => request.RequestUri?.AbsolutePath switch
		{
			_ when request.RequestUri == PlaylistUri => Responses.Text("#EXTM3U\n#EXTINF:1,\na.ts\n#EXTINF:1,\nb.ts\n" + EndList),
			{ } path when path.EndsWith("b.ts", StringComparison.Ordinal) && ++attempts < HlsStreamSession.MaxAttempts => Responses.Status(HttpStatusCode.ServiceUnavailable),
			_ => Segment(request)
		});
		ShortDelayTimeProvider time = NewTime();

		CopyResult copy = await CopyAsync(handler, time, cancellationToken);

		long segment = MediaSamples.TransportStream("a").Length;

		using (Assert.Multiple())
		{
			await Assert.That(copy.Output).IsEqualTo("[a][b]");
			await Assert.That(copy.Progress).IsEquivalentTo([segment, segment], CollectionOrdering.Matching);
			await Assert.That(attempts).IsEqualTo(HlsStreamSession.MaxAttempts);
			await Assert.That(time.Delays).IsEquivalentTo(Enumerable.Repeat(HlsStreamSession.RetryDelay, HlsStreamSession.MaxAttempts - 1), CollectionOrdering.Matching);
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task RefreshesWhileASegmentIsSlowSoLaterSegmentsAreNotLostAsync(CancellationToken cancellationToken)
	{
		string[] playlists =
		[
			Head + "#EXT-X-MEDIA-SEQUENCE:1\n1.ts\n2.ts\n",
			Head + "#EXT-X-MEDIA-SEQUENCE:2\n2.ts\n3.ts\n",
			Head + "#EXT-X-MEDIA-SEQUENCE:3\n3.ts\n4.ts\n",
			Head + "#EXT-X-MEDIA-SEQUENCE:4\n4.ts\n5.ts\n" + EndList
		];
		int playlistIndex = 0;
		TaskCompletionSource slidPast = new(TaskCreationOptions.RunContinuationsAsynchronously);
		SegmentGates gates = new(1, "2");
		FakeHttpMessageHandler handler = new(async (request, ct) =>
		{
			if (request.RequestUri != PlaylistUri)
			{
				return await gates.PassAsync(request, ct);
			}

			// The last playlist no longer lists segment 3, which only an earlier refresh could have found
			int index = Math.Min(Interlocked.Increment(ref playlistIndex) - 1, playlists.Length - 1);

			if (index == playlists.Length - 1)
			{
				slidPast.TrySetResult();
			}

			return Responses.Text(playlists[index]);
		});

		Task<CopyResult> copying = CopyAsync(handler, NewTime(), cancellationToken);
		await slidPast.Task.WaitAsync(cancellationToken);
		gates.ReleaseAll();
		CopyResult copy = await copying;

		using (Assert.Multiple())
		{
			await Assert.That((copy.Output, copy.Reason, copy.MissedSegments)).IsEqualTo(("[1][2][3][4][5]", StreamEndReason.EndOfStream, 0L));
			await Assert.That(handler.RequestsTo("/live-bvc/1/live_2500/2.ts")).Count().IsEqualTo(1);
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task DownloadsAtMostTheAllowedNumberOfSegmentsAtOnceAsync(CancellationToken cancellationToken)
	{
		string[] later = ["2", "3", "4", "5", "6", "7", "8"];
		SegmentGates gates = new(HlsStreamSession.MaxConcurrentDownloads, later);
		FakeHttpMessageHandler handler = new(async (request, ct) => request.RequestUri == PlaylistUri
			? Responses.Text($"#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:1\n1.ts\n{string.Concat(later.Select(static name => $"{name}.ts\n"))}{EndList}")
			: await gates.PassAsync(request, ct));

		Task<CopyResult> copying = CopyAsync(handler, NewTime(), cancellationToken);
		await gates.Reached.WaitAsync(cancellationToken);
		gates.ReleaseAll();
		CopyResult copy = await copying;

		using (Assert.Multiple())
		{
			await Assert.That(copy.Output).IsEqualTo("[1][2][3][4][5][6][7][8]");
			await Assert.That(gates.Peak).IsEqualTo(HlsStreamSession.MaxConcurrentDownloads);
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task WritesAndReportsSegmentsInPlaylistOrderWhenLaterOnesArriveFirstAsync(CancellationToken cancellationToken)
	{
		SegmentGates gates = new(3, "2", "3", "4");
		using HttpClient client = new(new FakeHttpMessageHandler(async (request, ct) => request.RequestUri == PlaylistUri
			? Responses.Text("#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:1\n1.ts\n2.ts\n3.ts\n4.ts\n" + EndList)
			: await gates.PassAsync(request, ct)));
		await using LiveStreamSession session = await HlsStreamSession.OpenAsync(client, PlaylistUri, RequestTimeout, NewTime(), cancellationToken);
		using MemoryStream output = new();
		SegmentLog segments = new(output);

		Task<StreamEndReason> copying = session.CopyToAsync(output, segments, null, cancellationToken);
		await gates.Reached.WaitAsync(cancellationToken);

		foreach (string name in (string[])["4", "3", "2"])
		{
			gates.Release(name);
			await gates.PassedAsync(name).WaitAsync(cancellationToken);
		}

		await copying;

		using (Assert.Multiple())
		{
			await Assert.That(MediaSamples.Markers(output.ToArray())).IsEqualTo("[1][2][3][4]");
			await Assert.That(segments.Entries).IsEquivalentTo(["media:[1]", "media:[2]", "media:[3]", "media:[4]"], CollectionOrdering.Matching);
			await Assert.That(segments.MatchedEveryWrite()).IsTrue();
		}
	}

	[Test]
	[Timeout(10_000)]
	[Arguments(Head + "#EXT-X-MEDIA-SEQUENCE:5\n5.ts\n6.ts\n" + EndList, "[1][2][5][6]", 2L)]
	[Arguments(Head + "#EXT-X-MEDIA-SEQUENCE:3\n3.ts\n4.ts\n" + EndList, "[1][2][3][4]", 0L)]
	[Arguments(Head + "#EXT-X-MEDIA-SEQUENCE:1\n1.ts\n2.ts\n3.ts\n" + EndList, "[1][2][3]", 0L)]
	// A restart may renumber the segments, and the new timeline ends the copy anyway
	[Arguments(Head + "#EXT-X-DISCONTINUITY-SEQUENCE:1\n#EXT-X-MEDIA-SEQUENCE:40\n40.ts\n" + EndList, "[1][2]", 0L)]
	public async Task ReportsSegmentsThatLeftTheWindowBeforeARefreshListedThemAsync(string refreshed, string expected, long missed, CancellationToken cancellationToken)
	{
		CopyResult copy = await CopyAsync(Serve(Head + "#EXT-X-MEDIA-SEQUENCE:1\n1.ts\n2.ts\n", refreshed), NewTime(), cancellationToken);

		await Assert.That((copy.Output, copy.MissedSegments)).IsEqualTo((expected, missed));
	}

	[Test]
	[Timeout(10_000)]
	[Arguments(typeof(HttpRequestException), true)]
	[Arguments(typeof(OperationCanceledException), false)]
	public async Task EndingTheCopyStopsEveryDownloadBeforeReturningAsync(Type expected, bool segmentFails, CancellationToken cancellationToken)
	{
		// When segment 2 fails, segments 3 and 4 are still downloading behind it
		SegmentGates gates = segmentFails ? new(2, "3", "4") : new(3, "2", "3", "4");
		FakeHttpMessageHandler handler = new(async (request, ct) =>
		{
			if (request.RequestUri == PlaylistUri)
			{
				return Responses.Text("#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:1\n1.ts\n2.ts\n3.ts\n4.ts\n5.ts\n" + EndList);
			}

			if (segmentFails && request.RequestUri?.AbsolutePath.EndsWith("/2.ts", StringComparison.Ordinal) is true)
			{
				await gates.Reached.WaitAsync(ct);
				return Responses.Status(HttpStatusCode.ServiceUnavailable);
			}

			return await gates.PassAsync(request, ct);
		});
		using HttpClient client = new(handler);
		await using LiveStreamSession session = await HlsStreamSession.OpenAsync(client, PlaylistUri, RequestTimeout, NewTime(), cancellationToken);
		using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		using MemoryStream output = new();

		Task<StreamEndReason> copying = session.CopyToAsync(output, null, stop.Token);
		await gates.Reached.WaitAsync(cancellationToken);

		if (!segmentFails)
		{
			await stop.CancelAsync();
		}

		await Assert.ThrowsAsync(expected, () => copying);

		using (Assert.Multiple())
		{
			await Assert.That(gates.Waiting).IsEqualTo(0);
			await Assert.That(gates.Cancelled).IsEqualTo(segmentFails ? 2 : 3);
			await Assert.That(handler.RequestsTo("/live-bvc/1/live_2500/5.ts")).IsEmpty();
			await Assert.That(MediaSamples.Markers(output.ToArray())).IsEqualTo("[1]");
		}
	}

	[Test]
	[Timeout(10_000)]
	[Arguments(false)]
	[Arguments(true)]
	public async Task FailingWriteOrObserverStopsEveryDownloadBeforeReturningAsync(bool observerFails, CancellationToken cancellationToken)
	{
		SegmentGates gates = new(2, "3", "4");
		FakeHttpMessageHandler handler = new(async (request, ct) => request.RequestUri == PlaylistUri
			? Responses.Text("#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:1\n1.ts\n2.ts\n3.ts\n4.ts\n5.ts\n" + EndList)
			: await gates.PassAsync(request, ct));
		using HttpClient client = new(handler);
		await using LiveStreamSession session = await HlsStreamSession.OpenAsync(client, PlaylistUri, RequestTimeout, NewTime(), cancellationToken);
		// Segments 3 and 4 are still downloading when segment 2 fails
		FailingOnSecondWrite output = new(observerFails ? null : gates.Reached);
		FailingObserver segments = new(observerFails ? gates.Reached : null);

		await Assert.ThrowsAsync<IOException>(() => session.CopyToAsync(output, segments, null, cancellationToken));

		using (Assert.Multiple())
		{
			await Assert.That(gates.Waiting).IsEqualTo(0);
			await Assert.That(gates.Cancelled).IsEqualTo(2);
			await Assert.That(handler.RequestsTo("/live-bvc/1/live_2500/5.ts")).IsEmpty();
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task RefreshFailingEveryAttemptThrowsAfterTheListedSegmentsAsync(CancellationToken cancellationToken)
	{
		int refreshes = 0;
		FakeHttpMessageHandler handler = new(request => request.RequestUri == PlaylistUri
			? Interlocked.Increment(ref refreshes) is 1 ? Responses.Text(Head + "a.ts\nb.ts\n") : Responses.Status(HttpStatusCode.ServiceUnavailable)
			: Segment(request));
		using HttpClient client = new(handler);
		await using LiveStreamSession session = await HlsStreamSession.OpenAsync(client, PlaylistUri, RequestTimeout, NewTime(), cancellationToken);
		using MemoryStream output = new();

		await Assert.ThrowsAsync<HttpRequestException>(() => session.CopyToAsync(output, null, cancellationToken));

		await Assert.That((MediaSamples.Markers(output.ToArray()), refreshes)).IsEqualTo(("[a][b]", 1 + HlsStreamSession.MaxAttempts));
	}

	private sealed record CopyResult(string Output, IEnumerable<long> Progress, StreamEndReason Reason, long MissedSegments);

	private sealed class FailingOnSecondWrite(Task? before) : MemoryStream
	{
		private int _writes;

		public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
		{
			if (++_writes is 2 && before is not null)
			{
				await before.WaitAsync(cancellationToken);
				throw new IOException("The disk is gone.");
			}

			await base.WriteAsync(buffer, cancellationToken);
		}
	}

	private sealed class FailingObserver(Task? before) : IHlsSegmentObserver
	{
		private int _segments;

		public void OnInitSegment(ReadOnlySpan<byte> data)
		{
		}

		public void OnMediaSegment(ReadOnlySpan<byte> data)
		{
			if (++_segments is 2 && before is not null)
			{
				before.Wait();
				throw new IOException("The splitter failed.");
			}
		}
	}

	private sealed class SegmentGates
	{
		private readonly Lock _lock = new();
		private readonly Dictionary<string, (TaskCompletionSource Gate, TaskCompletionSource Passed)> _segments;
		private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly int _target;
		private int _waiting;
		private int _peak;
		private int _cancelled;

		/// <param name="target">How many downloads must wait at once before <see cref="Reached"/> completes.</param>
		public SegmentGates(int target, params string[] names)
		{
			_target = target;
			_segments = names.ToDictionary(name => name, _ => (new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)));
		}

		public Task Reached => _reached.Task;

		public int Waiting
		{
			get
			{
				lock (_lock)
				{
					return _waiting;
				}
			}
		}

		public int Peak
		{
			get
			{
				lock (_lock)
				{
					return _peak;
				}
			}
		}

		public int Cancelled
		{
			get
			{
				lock (_lock)
				{
					return _cancelled;
				}
			}
		}

		public void Release(string name)
		{
			_segments[name].Gate.SetResult();
		}

		public void ReleaseAll()
		{
			foreach ((TaskCompletionSource gate, _) in _segments.Values)
			{
				gate.TrySetResult();
			}
		}

		public Task PassedAsync(string name)
		{
			return _segments[name].Passed.Task;
		}

		public async Task<HttpResponseMessage> PassAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			string name = Path.GetFileNameWithoutExtension(request.RequestUri?.AbsolutePath ?? string.Empty);

			if (_segments.TryGetValue(name, out (TaskCompletionSource Gate, TaskCompletionSource Passed) segment))
			{
				lock (_lock)
				{
					_peak = Math.Max(_peak, ++_waiting);

					if (_waiting >= _target)
					{
						_reached.TrySetResult();
					}
				}

				try
				{
					await segment.Gate.Task.WaitAsync(cancellationToken);
				}
				catch (OperationCanceledException)
				{
					lock (_lock)
					{
						++_cancelled;
					}

					throw;
				}
				finally
				{
					lock (_lock)
					{
						--_waiting;
					}
				}

				segment.Passed.TrySetResult();
			}

			return Segment(request);
		}
	}

	private sealed class GatedStream(byte[] content, TaskCompletionSource headersSent, Task released) : Stream
	{
		private readonly MemoryStream _content = new(content);

		public override bool CanRead => true;

		public override bool CanSeek => false;

		public override bool CanWrite => false;

		public override long Length => throw new NotSupportedException();

		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			headersSent.TrySetResult();
			await released.WaitAsync(cancellationToken);
			return await _content.ReadAsync(buffer, cancellationToken);
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException();
		}

		public override void Flush()
		{
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			throw new NotSupportedException();
		}

		public override void SetLength(long value)
		{
			throw new NotSupportedException();
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException();
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				_content.Dispose();
			}

			base.Dispose(disposing);
		}
	}

	// Each report must describe exactly the bytes the destination receives next
	private sealed class SegmentLog(MemoryStream output) : IHlsSegmentObserver
	{
		private byte[]? _expected;
		private long _expectedAt;
		private bool _matched = true;

		public List<string> Entries { get; } = [];

		public long Length { get; private set; }

		public bool MatchedEveryWrite()
		{
			Verify();
			return _matched;
		}

		public void OnInitSegment(ReadOnlySpan<byte> data)
		{
			Record("init", data);
		}

		public void OnMediaSegment(ReadOnlySpan<byte> data)
		{
			Record("media", data);
		}

		private void Record(string kind, ReadOnlySpan<byte> data)
		{
			Verify();
			Entries.Add($"{kind}:{MediaSamples.Markers(data.ToArray())}");
			(_expected, _expectedAt) = (data.ToArray(), output.Length);
			Length += data.Length;
		}

		private void Verify()
		{
			if (_expected is { } expected)
			{
				_matched &= output.Length == _expectedAt + expected.Length && output.ToArray().AsSpan((int)_expectedAt).SequenceEqual(expected);
			}
		}
	}
}
