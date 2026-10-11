using BilibiliLive.Streams;
using BilibiliLive.Tests.Fakes;
using System.Net;
using System.Text;

namespace BilibiliLive.Tests.Streams;

public class HlsStreamSessionTest
{
	private static readonly Uri PlaylistUri = new("https://cdn.test/live-bvc/1/live_2500/index.m3u8?expires=1&sign=x");

	private static HttpResponseMessage Segment(string name)
	{
		return Responses.Bytes(Encoding.ASCII.GetBytes($"[{name}]"));
	}

	private static async Task<(string Output, RecordingProgress Progress)> CopyAsync(HttpClient client, ManualTimeProvider time, CancellationToken cancellationToken)
	{
		await using LiveStreamSession session = await HlsStreamSession.OpenAsync(client, PlaylistUri, time, cancellationToken);
		using MemoryStream output = new();
		RecordingProgress progress = new();

		await session.CopyToAsync(output, progress, cancellationToken);

		return (Encoding.ASCII.GetString(output.ToArray()), progress);
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
			{ } path when path.StartsWith("/live-bvc/1/live_2500/", StringComparison.Ordinal) => Segment(Path.GetFileNameWithoutExtension(path)),
			_ => Responses.NotFound()
		});
		using HttpClient client = new(handler);
		ManualTimeProvider time = new();

		(string output, RecordingProgress progress) = await CopyAsync(client, time, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(output).IsEqualTo("[h1][1][2][3][4]");
			await Sequences.AssertEqualAsync(progress.Reports, [4L, 3L, 3L, 3L, 3L]);
			await Assert.That(handler.RequestsTo(PlaylistUri.AbsolutePath).Count()).IsEqualTo(3);
			await Assert.That(handler.RequestsTo("/live-bvc/1/live_2500/2.m4s").Count()).IsEqualTo(1);
			await Sequences.AssertEqualAsync(time.Delays, [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)]);
		}
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
			: Segment(Path.GetFileNameWithoutExtension(request.RequestUri?.AbsolutePath ?? string.Empty)));
		using HttpClient client = new(handler);

		(string output, _) = await CopyAsync(client, new ManualTimeProvider(), cancellationToken);

		await Assert.That(output).IsEqualTo("[h1][1][2]");
	}

	[Test]
	[Timeout(10_000)]
	public async Task NewSessionSkipsFragmentsOfAnOlderMapAsync(CancellationToken cancellationToken)
	{
		FakeHttpMessageHandler handler = new(request => request.RequestUri == PlaylistUri
			? Responses.Text("#EXTM3U\n#EXT-X-MAP:URI=\"h1.m4s\"\n1.m4s\n#EXT-X-MAP:URI=\"h2.m4s\"\n2.m4s\n3.m4s\n#EXT-X-ENDLIST\n")
			: Segment(Path.GetFileNameWithoutExtension(request.RequestUri?.AbsolutePath ?? string.Empty)));
		using HttpClient client = new(handler);

		(string output, _) = await CopyAsync(client, new ManualTimeProvider(), cancellationToken);

		await Assert.That(output).IsEqualTo("[h2][2][3]");
	}

	[Test]
	[Timeout(10_000)]
	public async Task RefreshRedirectedToAnotherNodeDoesNotRewriteSegmentsAsync(CancellationToken cancellationToken)
	{
		(string Host, string Content)[] playlists =
		[
			("node-a.test", "#EXTM3U\n#EXT-X-TARGETDURATION:2\n100.m4s\n101.m4s\n"),
			("node-b.test", "#EXTM3U\n#EXT-X-TARGETDURATION:2\n101.m4s\n102.m4s\n#EXT-X-ENDLIST\n")
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

			return Segment(Path.GetFileNameWithoutExtension(request.RequestUri?.AbsolutePath ?? string.Empty));
		});
		using HttpClient client = new(handler);

		(string output, _) = await CopyAsync(client, new ManualTimeProvider(), cancellationToken);

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

			return request.RequestUri?.Host is "edge.test" ? Segment(request.RequestUri.PathAndQuery) : Responses.NotFound();
		});
		using HttpClient client = new(handler);

		(string output, _) = await CopyAsync(client, new ManualTimeProvider(), cancellationToken);

		await Assert.That(output).IsEqualTo("[/node/7/seg-1.ts][/node/shared/seg-2.ts?part=2]");
	}

	[Test]
	[Timeout(10_000)]
	public async Task MissingTargetDurationPollsEverySecondAsync(CancellationToken cancellationToken)
	{
		int playlistIndex = 0;
		FakeHttpMessageHandler handler = new(request => request.RequestUri == PlaylistUri
			? Responses.Text(playlistIndex++ is 0 ? "#EXTM3U\n#EXTINF:1,\na.ts\n" : "#EXTM3U\n#EXTINF:1,\na.ts\n#EXT-X-ENDLIST\n")
			: Segment("a"));
		using HttpClient client = new(handler);
		ManualTimeProvider time = new();

		(string output, _) = await CopyAsync(client, time, cancellationToken);

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
		FakeHttpMessageHandler handler = new(request => request.RequestUri == PlaylistUri
			? Responses.Text("#EXTM3U\n#EXTINF:1,\na.ts\n#EXT-X-ENDLIST\n")
			: ++attempts < HlsStreamSession.MaxAttempts ? Responses.Status(HttpStatusCode.ServiceUnavailable) : Segment("a"));
		using HttpClient client = new(handler);
		ManualTimeProvider time = new();

		(string output, RecordingProgress progress) = await CopyAsync(client, time, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(output).IsEqualTo("[a]");
			await Sequences.AssertEqualAsync(progress.Reports, [3L]);
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
			: request.RequestUri?.AbsolutePath.EndsWith("ok.ts", StringComparison.Ordinal) is true ? Segment("ok") : Responses.Status(HttpStatusCode.ServiceUnavailable));
		using HttpClient client = new(handler);
		await using LiveStreamSession session = await HlsStreamSession.OpenAsync(client, PlaylistUri, new ManualTimeProvider(), cancellationToken);
		using MemoryStream output = new();

		await Assert.ThrowsAsync<HttpRequestException>(() => session.CopyToAsync(output, null, cancellationToken));

		using (Assert.Multiple())
		{
			await Assert.That(Encoding.ASCII.GetString(output.ToArray())).IsEqualTo("[ok]");
			await Assert.That(handler.Requests.Count(r => r.Uri.AbsolutePath.EndsWith("bad.ts", StringComparison.Ordinal))).IsEqualTo(HlsStreamSession.MaxAttempts);
		}
	}
}
