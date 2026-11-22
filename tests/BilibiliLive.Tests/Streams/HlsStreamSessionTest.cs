using BilibiliLive.Streams;
using BilibiliLive.Tests.Fakes;
using System.Net;

namespace BilibiliLive.Tests.Streams;

public class HlsStreamSessionTest
{
	private static readonly Uri PlaylistUri = new("https://cdn.test/live-bvc/1/live_2500/index.m3u8?expires=1&sign=x");
	private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

	private static HttpResponseMessage Segment(HttpRequestMessage request)
	{
		string path = request.RequestUri?.AbsolutePath ?? string.Empty;
		return Responses.Bytes(MediaSamples.ForPath(path, Path.GetFileNameWithoutExtension(path)));
	}

	private static async Task<(string Output, RecordingProgress Progress, StreamEndReason Reason)> CopyAsync(HttpClient client, ManualTimeProvider time, CancellationToken cancellationToken, TimeSpan? requestTimeout = null)
	{
		await using LiveStreamSession session = await HlsStreamSession.OpenAsync(client, PlaylistUri, requestTimeout ?? RequestTimeout, time, cancellationToken);
		using MemoryStream output = new();
		RecordingProgress progress = new();

		StreamEndReason reason = await session.CopyToAsync(output, progress, cancellationToken);

		return (MediaSamples.Markers(output.ToArray()), progress, reason);
	}

	[Test]
	[Timeout(10_000)]
	public async Task WritesMapOnceDeduplicatesSegmentsAndStopsAtEndListAsync(CancellationToken cancellationToken)
	{
		string[] playlists =
		[
			"#EXTM3U\n#EXT-X-VERSION:7\n#EXT-X-TARGETDURATION:2\n#EXT-X-MAP:URI=\"h1.m4s\"\n#EXTINF:1.00,\n1.m4s\n#EXTINF:1.00,\n2.m4s\n",
			"#EXTM3U\n#EXT-X-VERSION:7\n#EXT-X-TARGETDURATION:2\n#EXT-X-MAP:URI=\"h1.m4s\"\n#EXTINF:1.00,\n2.m4s\n#EXTINF:1.00,\n3.m4s\n",
			"#EXTM3U\n#EXT-X-VERSION:7\n#EXT-X-TARGETDURATION:2\n#EXT-X-MAP:URI=\"h1.m4s\"\n#EXTINF:1.00,\n3.m4s\n#EXTINF:1.00,\n4.m4s\n#EXT-X-ENDLIST\n"
		];
		int playlistIndex = 0;
		FakeHttpMessageHandler handler = new(request => request.RequestUri?.AbsolutePath switch
		{
			"/live-bvc/1/live_2500/index.m3u8" => Responses.Text(playlists[Math.Min(playlistIndex++, playlists.Length - 1)]),
			{ } path when path.StartsWith("/live-bvc/1/live_2500/", StringComparison.Ordinal) => Segment(request),
			_ => Responses.NotFound()
		});
		using HttpClient client = new(handler);
		ManualTimeProvider time = new();

		(string output, RecordingProgress progress, _) = await CopyAsync(client, time, cancellationToken);

		long init = MediaSamples.Fmp4Init("h1").Length;
		long fragment = MediaSamples.Fmp4Fragment("1").Length;

		using (Assert.Multiple())
		{
			await Assert.That(output).IsEqualTo("[h1][1][2][3][4]");
			await Sequences.AssertEqualAsync(progress.Reports, [init, fragment, fragment, fragment, fragment]);
			await Assert.That(handler.RequestsTo(PlaylistUri.AbsolutePath).Count()).IsEqualTo(3);
			await Assert.That(handler.RequestsTo("/live-bvc/1/live_2500/2.m4s").Count()).IsEqualTo(1);
			await Sequences.AssertEqualAsync(time.Delays, [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)]);
		}
	}

	[Test]
	[Timeout(10_000)]
	[Arguments("#EXTM3U\n#EXT-X-MAP:URI=\"h1.m4s\"\n1.m4s\n2.m4s\n#EXT-X-ENDLIST\n", "init:[h1] media:[1] media:[2]")]
	[Arguments("#EXTM3U\n1.ts\n2.ts\n#EXT-X-ENDLIST\n", "media:[1] media:[2]")]
	public async Task ReportsEachSegmentRightBeforeWritingExactlyItsBytesAsync(string playlist, string expected, CancellationToken cancellationToken)
	{
		using HttpClient client = new(new FakeHttpMessageHandler(request => request.RequestUri == PlaylistUri ? Responses.Text(playlist) : Segment(request)));
		await using LiveStreamSession session = await HlsStreamSession.OpenAsync(client, PlaylistUri, RequestTimeout, new ManualTimeProvider(), cancellationToken);
		using MemoryStream output = new();
		SegmentLog segments = new(output);

		await session.CopyToAsync(output, segments, null, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(string.Join(" ", segments.Entries)).IsEqualTo(expected);
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
			"#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXT-X-MAP:URI=\"h1.m4s\"\n2.m4s\n3.m4s\n#EXT-X-ENDLIST\n"
		];
		int playlistIndex = 0;
		FakeHttpMessageHandler handler = new(request => request.RequestUri?.AbsolutePath switch
		{
			"/live-bvc/1/live_2500/index.m3u8" => Responses.Text($"#EXTM3U\n#EXT-X-VERSION:7\n#EXT-X-STREAM-INF:PROGRAM-ID=1,BANDWIDTH=2560000\nhttps://edge.test{variantPath}?sign=y\n"),
			variantPath => Responses.Text(playlists[Math.Min(playlistIndex++, playlists.Length - 1)]),
			{ } path when path.StartsWith("/edge/live-bvc/1/live_2500/", StringComparison.Ordinal) => Segment(request),
			_ => Responses.NotFound()
		});
		using HttpClient client = new(handler);

		(string output, _, StreamEndReason reason) = await CopyAsync(client, new ManualTimeProvider(), cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(output).IsEqualTo("[h1][1][2][3]");
			await Assert.That(reason).IsEqualTo(StreamEndReason.EndOfStream);
			await Assert.That(handler.RequestsTo(PlaylistUri.AbsolutePath).Count()).IsEqualTo(1);
			await Assert.That(handler.RequestsTo(variantPath).Count()).IsEqualTo(2);
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task ValidatedFirstSegmentAndMapAreNotDownloadedAgainAsync(CancellationToken cancellationToken)
	{
		FakeHttpMessageHandler handler = new(request => request.RequestUri == PlaylistUri
			? Responses.Text("#EXTM3U\n#EXT-X-MAP:URI=\"h1.m4s\"\n1.m4s\n2.m4s\n#EXT-X-ENDLIST\n")
			: Segment(request));
		using HttpClient client = new(handler);

		(string output, _, _) = await CopyAsync(client, new ManualTimeProvider(), cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(output).IsEqualTo("[h1][1][2]");
			await Assert.That(handler.RequestsTo("/live-bvc/1/live_2500/h1.m4s").Count()).IsEqualTo(1);
			await Assert.That(handler.RequestsTo("/live-bvc/1/live_2500/1.m4s").Count()).IsEqualTo(1);
		}
	}

	[Test]
	[Arguments("#EXTM3U\n#EXT-X-TARGETDURATION:2\n")]
	[Arguments("#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXT-X-ENDLIST\n")]
	public async Task PlaylistWithoutSegmentsIsRejectedAsync(string playlist, CancellationToken cancellationToken)
	{
		using HttpClient client = new(new FakeHttpMessageHandler(request => request.RequestUri == PlaylistUri ? Responses.Text(playlist) : Segment(request)));

		await Assert.ThrowsAsync<InvalidDataException>(() => HlsStreamSession.OpenAsync(client, PlaylistUri, RequestTimeout, new ManualTimeProvider(), cancellationToken));
	}

	[Test]
	[Arguments("#EXTM3U\n#EXTINF:1,\nbad.ts\n", "bad.ts")]
	[Arguments("#EXTM3U\n#EXT-X-MAP:URI=\"h1.m4s\"\n#EXTINF:1,\nbad.m4s\n", "bad.m4s")]
	[Arguments("#EXTM3U\n#EXT-X-MAP:URI=\"bad.mp4\"\n#EXTINF:1,\n1.m4s\n", "bad.mp4")]
	public async Task InvalidFirstSegmentOrMapIsRejectedAsync(string playlist, string invalid, CancellationToken cancellationToken)
	{
		using HttpClient client = new(new FakeHttpMessageHandler(request => request.RequestUri switch
		{
			{ } uri when uri == PlaylistUri => Responses.Text(playlist),
			{ } uri when uri.AbsolutePath.EndsWith(invalid, StringComparison.Ordinal) => Responses.Text("<html>blocked</html>"),
			_ => Segment(request)
		}));

		await Assert.ThrowsAsync<InvalidDataException>(() => HlsStreamSession.OpenAsync(client, PlaylistUri, RequestTimeout, new ManualTimeProvider(), cancellationToken));
	}

	[Test]
	[Timeout(10_000)]
	public async Task ChangedMapEndsTheCopyAsync(CancellationToken cancellationToken)
	{
		string[] playlists =
		[
			"#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXT-X-MAP:URI=\"h1.m4s\"\n1.m4s\n2.m4s\n",
			"#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXT-X-MAP:URI=\"h1.m4s\"\n2.m4s\n#EXT-X-MAP:URI=\"h2.m4s\"\n3.m4s\n"
		];
		int playlistIndex = 0;
		FakeHttpMessageHandler handler = new(request => request.RequestUri == PlaylistUri
			? Responses.Text(playlists[Math.Min(playlistIndex++, playlists.Length - 1)])
			: Segment(request));
		using HttpClient client = new(handler);

		(string output, _, StreamEndReason reason) = await CopyAsync(client, new ManualTimeProvider(), cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(output).IsEqualTo("[h1][1][2]");
			await Assert.That(reason).IsEqualTo(StreamEndReason.Discontinuity);
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task DiscontinuityEndsTheCopyBeforeTheNewTimelineAsync(CancellationToken cancellationToken)
	{
		string[] playlists =
		[
			"#EXTM3U\n#EXT-X-TARGETDURATION:2\na.ts\nb.ts\n",
			"#EXTM3U\n#EXT-X-TARGETDURATION:2\nb.ts\n#EXT-X-DISCONTINUITY\nc.ts\n"
		];
		int playlistIndex = 0;
		FakeHttpMessageHandler handler = new(request => request.RequestUri == PlaylistUri
			? Responses.Text(playlists[Math.Min(playlistIndex++, playlists.Length - 1)])
			: Segment(request));
		using HttpClient client = new(handler);

		(string output, _, StreamEndReason reason) = await CopyAsync(client, new ManualTimeProvider(), cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(output).IsEqualTo("[a][b]");
			await Assert.That(reason).IsEqualTo(StreamEndReason.Discontinuity);
			await Assert.That(handler.RequestsTo("/live-bvc/1/live_2500/c.ts").Count()).IsEqualTo(0);
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task ChangedDiscontinuitySequenceEndsTheCopyAsync(CancellationToken cancellationToken)
	{
		string[] playlists =
		[
			"#EXTM3U\n#EXT-X-DISCONTINUITY-SEQUENCE:3\na.ts\n",
			"#EXTM3U\n#EXT-X-DISCONTINUITY-SEQUENCE:5\nb.ts\n"
		];
		int playlistIndex = 0;
		FakeHttpMessageHandler handler = new(request => request.RequestUri == PlaylistUri
			? Responses.Text(playlists[Math.Min(playlistIndex++, playlists.Length - 1)])
			: Segment(request));
		using HttpClient client = new(handler);

		(string output, _, StreamEndReason reason) = await CopyAsync(client, new ManualTimeProvider(), cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(output).IsEqualTo("[a]");
			await Assert.That(reason).IsEqualTo(StreamEndReason.Discontinuity);
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task NewSessionStartsAtTheLatestTimelineAndFollowsTheSlidingWindowAsync(CancellationToken cancellationToken)
	{
		string[] playlists =
		[
			"#EXTM3U\n#EXT-X-DISCONTINUITY-SEQUENCE:0\na.ts\n#EXT-X-DISCONTINUITY\nb.ts\n",
			"#EXTM3U\n#EXT-X-DISCONTINUITY-SEQUENCE:1\nb.ts\nc.ts\n#EXT-X-ENDLIST\n"
		];
		int playlistIndex = 0;
		FakeHttpMessageHandler handler = new(request => request.RequestUri == PlaylistUri
			? Responses.Text(playlists[Math.Min(playlistIndex++, playlists.Length - 1)])
			: Segment(request));
		using HttpClient client = new(handler);

		(string output, _, StreamEndReason reason) = await CopyAsync(client, new ManualTimeProvider(), cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(output).IsEqualTo("[b][c]");
			await Assert.That(reason).IsEqualTo(StreamEndReason.EndOfStream);
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task HungSegmentRequestTimesOutAndIsRetriedAsync(CancellationToken cancellationToken)
	{
		int attempts = 0;
		FakeHttpMessageHandler handler = new(async (request, ct) =>
		{
			if (request.RequestUri == PlaylistUri)
			{
				return Responses.Text("#EXTM3U\na.ts\nb.ts\n#EXT-X-ENDLIST\n");
			}

			if (request.RequestUri?.AbsolutePath.EndsWith("b.ts", StringComparison.Ordinal) is true && Interlocked.Increment(ref attempts) is 1)
			{
				await Task.Delay(Timeout.Infinite, ct);
			}

			return Segment(request);
		});
		using HttpClient client = new(handler);

		(string output, _, _) = await CopyAsync(client, new ManualTimeProvider(), cancellationToken, TimeSpan.FromMilliseconds(200));

		using (Assert.Multiple())
		{
			await Assert.That(output).IsEqualTo("[a][b]");
			await Assert.That(attempts).IsEqualTo(2);
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task SegmentThatIsNotMediaIsRetriedThenFailsAsync(CancellationToken cancellationToken)
	{
		FakeHttpMessageHandler handler = new(request => request.RequestUri?.AbsolutePath switch
		{
			_ when request.RequestUri == PlaylistUri => Responses.Text("#EXTM3U\n#EXT-X-MAP:URI=\"h1.m4s\"\n1.m4s\nbad.m4s\n#EXT-X-ENDLIST\n"),
			{ } path when path.EndsWith("bad.m4s", StringComparison.Ordinal) => Responses.Text("<html>blocked</html>"),
			_ => Segment(request)
		});
		using HttpClient client = new(handler);
		await using LiveStreamSession session = await HlsStreamSession.OpenAsync(client, PlaylistUri, RequestTimeout, new ManualTimeProvider(), cancellationToken);
		using MemoryStream output = new();

		await Assert.ThrowsAsync<InvalidDataException>(() => session.CopyToAsync(output, null, cancellationToken));

		using (Assert.Multiple())
		{
			await Assert.That(MediaSamples.Markers(output.ToArray())).IsEqualTo("[h1][1]");
			await Assert.That(handler.RequestsTo("/live-bvc/1/live_2500/bad.m4s").Count()).IsEqualTo(HlsStreamSession.MaxAttempts);
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task NewSessionSkipsFragmentsOfAnOlderMapAsync(CancellationToken cancellationToken)
	{
		FakeHttpMessageHandler handler = new(request => request.RequestUri == PlaylistUri
			? Responses.Text("#EXTM3U\n#EXT-X-MAP:URI=\"h1.m4s\"\n1.m4s\n#EXT-X-MAP:URI=\"h2.m4s\"\n2.m4s\n3.m4s\n#EXT-X-ENDLIST\n")
			: Segment(request));
		using HttpClient client = new(handler);

		(string output, _, _) = await CopyAsync(client, new ManualTimeProvider(), cancellationToken);

		await Assert.That(output).IsEqualTo("[h2][2][3]");
	}

	[Test]
	[Timeout(10_000)]
	public async Task RefreshRedirectedToAnotherNodeDoesNotRewriteSegmentsAsync(CancellationToken cancellationToken)
	{
		(string Host, string Content)[] playlists =
		[
			("node-a.test", "#EXTM3U\n#EXT-X-TARGETDURATION:2\n100.ts\n101.ts\n"),
			("node-b.test", "#EXTM3U\n#EXT-X-TARGETDURATION:2\n101.ts\n102.ts\n#EXT-X-ENDLIST\n")
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
		using HttpClient client = new(handler);

		(string output, _, _) = await CopyAsync(client, new ManualTimeProvider(), cancellationToken);

		await Assert.That(output).IsEqualTo("[100][101][102]");
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
				HttpResponseMessage response = Responses.Text("#EXTM3U\n#EXTINF:1,\nseg-1.ts\n#EXTINF:1,\n../shared/seg-2.ts?part=2\n#EXT-X-ENDLIST\n");
				response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, redirected);
				return response;
			}

			return request.RequestUri?.Host is "edge.test" ? Responses.Bytes(MediaSamples.TransportStream(request.RequestUri.PathAndQuery)) : Responses.NotFound();
		});
		using HttpClient client = new(handler);

		(string output, _, _) = await CopyAsync(client, new ManualTimeProvider(), cancellationToken);

		await Assert.That(output).IsEqualTo("[/node/7/seg-1.ts][/node/shared/seg-2.ts?part=2]");
	}

	[Test]
	[Timeout(10_000)]
	public async Task MissingTargetDurationPollsEverySecondAsync(CancellationToken cancellationToken)
	{
		int playlistIndex = 0;
		FakeHttpMessageHandler handler = new(request => request.RequestUri == PlaylistUri
			? Responses.Text(playlistIndex++ is 0 ? "#EXTM3U\n#EXTINF:1,\na.ts\n" : "#EXTM3U\n#EXTINF:1,\na.ts\n#EXT-X-ENDLIST\n")
			: Segment(request));
		using HttpClient client = new(handler);
		ManualTimeProvider time = new();

		(string output, _, _) = await CopyAsync(client, time, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(output).IsEqualTo("[a]");
			await Sequences.AssertEqualAsync(time.Delays, [HlsStreamSession.DefaultPollInterval]);
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task FailedSegmentIsRetriedAndWrittenOnceAsync(CancellationToken cancellationToken)
	{
		int attempts = 0;
		FakeHttpMessageHandler handler = new(request => request.RequestUri?.AbsolutePath switch
		{
			_ when request.RequestUri == PlaylistUri => Responses.Text("#EXTM3U\n#EXTINF:1,\na.ts\n#EXTINF:1,\nb.ts\n#EXT-X-ENDLIST\n"),
			{ } path when path.EndsWith("b.ts", StringComparison.Ordinal) && ++attempts < HlsStreamSession.MaxAttempts => Responses.Status(HttpStatusCode.ServiceUnavailable),
			_ => Segment(request)
		});
		using HttpClient client = new(handler);
		ManualTimeProvider time = new();

		(string output, RecordingProgress progress, _) = await CopyAsync(client, time, cancellationToken);

		long segment = MediaSamples.TransportStream("a").Length;

		using (Assert.Multiple())
		{
			await Assert.That(output).IsEqualTo("[a][b]");
			await Sequences.AssertEqualAsync(progress.Reports, [segment, segment]);
			await Assert.That(attempts).IsEqualTo(HlsStreamSession.MaxAttempts);
			await Sequences.AssertEqualAsync(time.Delays, Enumerable.Repeat(HlsStreamSession.RetryDelay, HlsStreamSession.MaxAttempts - 1));
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task SegmentFailingEveryAttemptThrowsAsync(CancellationToken cancellationToken)
	{
		FakeHttpMessageHandler handler = new(request => request.RequestUri == PlaylistUri
			? Responses.Text("#EXTM3U\n#EXTINF:1,\nok.ts\n#EXTINF:1,\nbad.ts\n#EXT-X-ENDLIST\n")
			: request.RequestUri?.AbsolutePath.EndsWith("ok.ts", StringComparison.Ordinal) is true ? Segment(request) : Responses.Status(HttpStatusCode.ServiceUnavailable));
		using HttpClient client = new(handler);
		await using LiveStreamSession session = await HlsStreamSession.OpenAsync(client, PlaylistUri, RequestTimeout, new ManualTimeProvider(), cancellationToken);
		using MemoryStream output = new();

		await Assert.ThrowsAsync<HttpRequestException>(() => session.CopyToAsync(output, null, cancellationToken));

		using (Assert.Multiple())
		{
			await Assert.That(MediaSamples.Markers(output.ToArray())).IsEqualTo("[ok]");
			await Assert.That(handler.Requests.Count(r => r.Uri.AbsolutePath.EndsWith("bad.ts", StringComparison.Ordinal))).IsEqualTo(HlsStreamSession.MaxAttempts);
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
