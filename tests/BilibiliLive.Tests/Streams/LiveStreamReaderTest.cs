using BilibiliLive.Streams;
using BilibiliLive.Tests.Fakes;

namespace BilibiliLive.Tests.Streams;

public class LiveStreamReaderTest
{
	private static readonly Uri BrokenHost = new("https://broken.test/live.flv?sign=1");
	private static readonly Uri WorkingHost = new("https://working.test/live.flv?sign=2");
	private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

	[Test]
	public async Task HttpStreamSkipsFailingHostAndCopiesBodyInBlocksAsync(CancellationToken cancellationToken)
	{
		byte[] body = new byte[300_000];
		new Random(42).NextBytes(body);
		"FLV"u8.CopyTo(body);
		FakeHttpMessageHandler handler = new(request => request.RequestUri == WorkingHost ? Responses.Bytes(body) : Responses.NotFound());
		using HttpClient client = new(handler);
		LiveStreamCandidate.Listed candidate = new(StreamProtocol.HttpStream, StreamFormat.Flv, StreamCodec.Avc, 10000, [], [BrokenHost, WorkingHost]);

		await using LiveStreamSession session = await LiveStreamReader.OpenAsync(client, candidate, StreamHostSelection.FirstResponse, ConnectTimeout, null, cancellationToken);
		using MemoryStream output = new();
		RecordingProgress progress = new();
		await session.CopyToAsync(output, progress, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(session.Source).IsEqualTo(WorkingHost);
			await Assert.That(output.ToArray().AsSpan().SequenceEqual(body)).IsTrue();
			await Assert.That(progress.Reports.Sum()).IsEqualTo(body.Length);
			await Assert.That(progress.Reports.Count).IsGreaterThan(1);
			await Sequences.AssertEquivalentAsync(handler.Requests.Select(r => r.Uri), [BrokenHost, WorkingHost]);
		}
	}

	[Test]
	public async Task HlsHostMustServeParsablePlaylistAsync(CancellationToken cancellationToken)
	{
		Uri garbage = new("https://garbage.test/index.m3u8");
		Uri valid = new("https://valid.test/index.m3u8");
		FakeHttpMessageHandler handler = new(request => request.RequestUri switch
		{
			{ Host: "garbage.test" } => Responses.Text("<html>blocked</html>"),
			{ Host: "valid.test", AbsolutePath: "/index.m3u8" } => Responses.Text("#EXTM3U\n#EXTINF:1,\nseg.ts\n#EXT-X-ENDLIST\n"),
			{ Host: "valid.test" } => Responses.Bytes(MediaSamples.TransportStream("segment")),
			_ => Responses.NotFound()
		});
		using HttpClient client = new(handler);
		LiveStreamCandidate.Listed candidate = new(StreamProtocol.HttpHls, StreamFormat.Ts, StreamCodec.Avc, 10000, [], [garbage, valid]);

		await using LiveStreamSession session = await LiveStreamReader.OpenAsync(client, candidate, StreamHostSelection.FirstResponse, ConnectTimeout, null, cancellationToken);
		using MemoryStream output = new();
		await session.CopyToAsync(output, null, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(session.Source).IsEqualTo(valid);
			await Assert.That(MediaSamples.Markers(output.ToArray())).IsEqualTo("[segment]");
		}
	}

	[Test]
	public async Task AllHostsFailingThrowsAsync(CancellationToken cancellationToken)
	{
		using HttpClient client = new(new FakeHttpMessageHandler(_ => Responses.NotFound()));
		LiveStreamCandidate.Listed candidate = new(StreamProtocol.HttpStream, StreamFormat.Flv, StreamCodec.Avc, 10000, [], [BrokenHost, WorkingHost]);

		AggregateException? exception = await Assert.ThrowsAsync<AggregateException>(() => LiveStreamReader.OpenAsync(client, candidate, StreamHostSelection.FastestResponse, ConnectTimeout, null, cancellationToken));

		await Assert.That(exception?.InnerExceptions.Count).IsEqualTo(2);
	}

	[Test]
	[Timeout(10_000)]
	public async Task HungHostOnlyUsesItsOwnConnectTimeoutAsync(CancellationToken cancellationToken)
	{
		FakeHttpMessageHandler handler = new(async (request, ct) =>
		{
			if (request.RequestUri == BrokenHost)
			{
				await Task.Delay(Timeout.Infinite, ct);
			}

			return Responses.Bytes([.. "FLV"u8]);
		});
		using HttpClient client = new(handler);
		LiveStreamCandidate.Listed candidate = new(StreamProtocol.HttpStream, StreamFormat.Flv, StreamCodec.Avc, 10000, [], [BrokenHost, WorkingHost]);
		List<(Uri Host, Exception Error)> failures = [];

		await using LiveStreamSession session = await LiveStreamReader.OpenAsync(client, candidate, StreamHostSelection.FirstResponse, TimeSpan.FromMilliseconds(200), (host, error) => failures.Add((host, error)), cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(session.Source).IsEqualTo(WorkingHost);
			await Assert.That(failures.Count).IsEqualTo(1);
			await Assert.That(failures[0].Host).IsEqualTo(BrokenHost);
			await Assert.That(failures[0].Error).IsTypeOf<TimeoutException>();
		}
	}

	[Test]
	public async Task EveryFailedHostIsReportedAsync(CancellationToken cancellationToken)
	{
		using HttpClient client = new(new FakeHttpMessageHandler(request => request.RequestUri == WorkingHost ? Responses.Bytes([.. "FLV"u8]) : Responses.NotFound()));
		Uri otherBroken = new("https://other-broken.test/live.flv");
		LiveStreamCandidate.Listed candidate = new(StreamProtocol.HttpStream, StreamFormat.Flv, StreamCodec.Avc, 10000, [], [BrokenHost, otherBroken, WorkingHost]);
		List<Uri> failed = [];

		await using LiveStreamSession session = await LiveStreamReader.OpenAsync(client, candidate, StreamHostSelection.FirstResponse, ConnectTimeout, (host, _) => failed.Add(host), cancellationToken);

		await Sequences.AssertEqualAsync(failed, [BrokenHost, otherBroken]);
	}

	[Test]
	[Arguments(new byte[0])]
	[Arguments(new byte[] { (byte)'<', (byte)'h', (byte)'t', (byte)'m', (byte)'l', (byte)'>' })]
	public async Task HttpStreamThatIsNotFlvIsRejectedAsync(byte[] body, CancellationToken cancellationToken)
	{
		using HttpClient client = new(new FakeHttpMessageHandler(_ => Responses.Bytes(body)));
		LiveStreamCandidate.Listed candidate = new(StreamProtocol.HttpStream, StreamFormat.Flv, StreamCodec.Avc, 10000, [], [WorkingHost]);
		List<Exception> failures = [];

		await Assert.ThrowsAsync<AggregateException>(() => LiveStreamReader.OpenAsync(client, candidate, StreamHostSelection.FirstResponse, ConnectTimeout, (_, error) => failures.Add(error), cancellationToken));

		await Assert.That(failures.Single()).IsTypeOf<InvalidDataException>();
	}

	[Test]
	[Timeout(10_000)]
	public async Task HttpStreamThatSendsNoDataWithinTheConnectTimeoutIsRejectedAsync(CancellationToken cancellationToken)
	{
		using HttpClient client = new(new FakeHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) }));
		LiveStreamCandidate.Listed candidate = new(StreamProtocol.HttpStream, StreamFormat.Flv, StreamCodec.Avc, 10000, [], [WorkingHost]);
		List<Exception> failures = [];

		await Assert.ThrowsAsync<AggregateException>(() => LiveStreamReader.OpenAsync(client, candidate, StreamHostSelection.FirstResponse, TimeSpan.FromMilliseconds(200), (_, error) => failures.Add(error), cancellationToken));

		await Assert.That(failures.Single()).IsTypeOf<TimeoutException>();
	}

	private sealed class StalledStream : Stream
	{
		public override bool CanRead => true;

		public override bool CanSeek => false;

		public override bool CanWrite => false;

		public override long Length => throw new NotSupportedException();

		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			await Task.Delay(Timeout.Infinite, cancellationToken);
			return 0;
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
	}
}
