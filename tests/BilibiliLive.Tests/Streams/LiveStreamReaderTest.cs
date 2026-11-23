using BilibiliLive.Streams;
using BilibiliLive.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;
using System.Diagnostics;

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

		await using LiveStreamSession session = await LiveStreamReader.OpenAsync(client, candidate, StreamHostSelection.FirstResponse, ConnectTimeout, cancellationToken: cancellationToken);
		using MemoryStream output = new();
		RecordingProgress progress = new();
		await session.CopyToAsync(output, progress, cancellationToken);
		byte[] copied = output.ToArray();

		using (Assert.Multiple())
		{
			await Assert.That(session.Source).IsEqualTo(WorkingHost);
			await Assert.That((copied.Length, copied.AsSpan().CommonPrefixLength(body))).IsEqualTo((body.Length, body.Length));
			await Assert.That(progress.Reports.Sum()).IsEqualTo(body.Length);
			await Assert.That(progress.Reports).Count().IsGreaterThan(1);
			await Assert.That(handler.Requests.Select(static r => r.Uri)).IsEquivalentTo([BrokenHost, WorkingHost], EqualityComparer<Uri>.Default);
		}
	}

	[Test]
	public async Task HlsHostMustServeParsablePlaylistAsync(CancellationToken cancellationToken)
	{
		Uri garbage = new("https://garbage.test/index.m3u8");
		Uri valid = new("https://valid.test/index.m3u8");
		FakeHttpMessageHandler handler = new(static request => request.RequestUri switch
		{
			{ Host: "garbage.test" } => Responses.Text("<html>blocked</html>"),
			{ Host: "valid.test", AbsolutePath: "/index.m3u8" } => Responses.Text("#EXTM3U\n#EXTINF:1,\nseg.ts\n#EXT-X-ENDLIST\n"),
			{ Host: "valid.test" } => Responses.Bytes(MediaSamples.TransportStream("segment")),
			_ => Responses.NotFound()
		});
		using HttpClient client = new(handler);
		LiveStreamCandidate.Listed candidate = new(StreamProtocol.HttpHls, StreamFormat.Ts, StreamCodec.Avc, 10000, [], [garbage, valid]);

		await using LiveStreamSession session = await LiveStreamReader.OpenAsync(client, candidate, StreamHostSelection.FirstResponse, ConnectTimeout, cancellationToken: cancellationToken);
		using MemoryStream output = new();
		await session.CopyToAsync(output, null, cancellationToken);

		await Assert.That((session.Source, MediaSamples.Markers(output.ToArray()))).IsEqualTo((valid, "[segment]"));
	}

	[Test]
	public async Task HungHostOnlyUsesItsOwnConnectTimeoutAsync(CancellationToken cancellationToken)
	{
		TaskCompletionSource hung = new(TaskCreationOptions.RunContinuationsAsynchronously);
		FakeHttpMessageHandler handler = new(async (request, ct) =>
		{
			if (request.RequestUri == BrokenHost)
			{
				hung.SetResult();
				await Task.Delay(Timeout.Infinite, ct);
			}

			return Responses.Bytes([.. "FLV"u8]);
		});
		using HttpClient client = new(handler);
		LiveStreamCandidate.Listed candidate = new(StreamProtocol.HttpStream, StreamFormat.Flv, StreamCodec.Avc, 10000, [], [BrokenHost, WorkingHost]);
		List<(Uri Host, Exception Error)> failures = [];
		FakeTimeProvider time = new();

		Task<LiveStreamSession> opening = LiveStreamReader.OpenAsync(client, candidate, StreamHostSelection.FirstResponse, ConnectTimeout, (failedHost, failure) => failures.Add((failedHost, failure)), time, cancellationToken);
		await hung.Task.WaitAsync(cancellationToken);
		time.Advance(ConnectTimeout);
		await using LiveStreamSession session = await opening;
		(Uri host, Exception error) = await Assert.That(failures).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(session.Source).IsEqualTo(WorkingHost);
			await Assert.That(host).IsEqualTo(BrokenHost);
			await Assert.That(error).IsTypeOf<TimeoutException>();
		}
	}

	[Test]
	public async Task EveryFailedHostIsReportedAsync(CancellationToken cancellationToken)
	{
		using HttpClient client = new(new FakeHttpMessageHandler(static request => request.RequestUri == WorkingHost ? Responses.Bytes([.. "FLV"u8]) : Responses.NotFound()));
		Uri otherBroken = new("https://other-broken.test/live.flv");
		LiveStreamCandidate.Listed candidate = new(StreamProtocol.HttpStream, StreamFormat.Flv, StreamCodec.Avc, 10000, [], [BrokenHost, otherBroken, WorkingHost]);
		List<Uri> failed = [];

		await using LiveStreamSession session = await LiveStreamReader.OpenAsync(client, candidate, StreamHostSelection.FirstResponse, ConnectTimeout, (host, _) => failed.Add(host), cancellationToken: cancellationToken);

		await Assert.That(failed).IsEquivalentTo([BrokenHost, otherBroken], EqualityComparer<Uri>.Default, CollectionOrdering.Matching);
	}

	[Test]
	[Arguments(new byte[0])]
	[Arguments(new[] { (byte)'<', (byte)'h', (byte)'t', (byte)'m', (byte)'l', (byte)'>' })]
	public async Task HttpStreamThatIsNotFlvIsRejectedAsync(byte[] body, CancellationToken cancellationToken)
	{
		using HttpClient client = new(new FakeHttpMessageHandler(_ => Responses.Bytes(body)));
		LiveStreamCandidate.Listed candidate = new(StreamProtocol.HttpStream, StreamFormat.Flv, StreamCodec.Avc, 10000, [], [WorkingHost]);
		List<Exception> failures = [];

		await Assert.ThrowsAsync<AggregateException>(() => LiveStreamReader.OpenAsync(client, candidate, StreamHostSelection.FirstResponse, ConnectTimeout, (_, error) => failures.Add(error), cancellationToken: cancellationToken));
		Exception failure = await Assert.That(failures).HasSingleItem();

		await Assert.That(failure).IsTypeOf<InvalidDataException>();
	}

	[Test]
	public async Task HttpStreamThatSendsNoDataWithinTheConnectTimeoutIsRejectedAsync(CancellationToken cancellationToken)
	{
		TaskCompletionSource stalled = new(TaskCreationOptions.RunContinuationsAsynchronously);
		using HttpClient client = new(new FakeHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StreamContent(new StalledStream(stalled)) }));
		LiveStreamCandidate.Listed candidate = new(StreamProtocol.HttpStream, StreamFormat.Flv, StreamCodec.Avc, 10000, [], [WorkingHost]);
		List<Exception> failures = [];
		FakeTimeProvider time = new();

		Task<LiveStreamSession> opening = LiveStreamReader.OpenAsync(client, candidate, StreamHostSelection.FirstResponse, ConnectTimeout, (_, error) => failures.Add(error), time, cancellationToken);
		await stalled.Task.WaitAsync(cancellationToken);
		time.Advance(ConnectTimeout);

		await Assert.ThrowsAsync<AggregateException>(() => opening);
		Exception failure = await Assert.That(failures).HasSingleItem();

		await Assert.That(failure).IsTypeOf<TimeoutException>();
	}

	[Test]
	[Arguments(StreamProtocol.HttpStream)]
	[Arguments(StreamProtocol.HttpHls)]
	public async Task CancelledOpenIsNotReportedAsAHostFailureAsync(StreamProtocol protocol, CancellationToken cancellationToken)
	{
		TaskCompletionSource hung = new(TaskCreationOptions.RunContinuationsAsynchronously);
		using HttpClient client = new(new FakeHttpMessageHandler(async (_, ct) =>
		{
			hung.TrySetResult();
			await Task.Delay(Timeout.Infinite, ct);
			throw new UnreachableException();
		}));
		LiveStreamCandidate.Listed candidate = new(protocol, StreamFormat.Ts, StreamCodec.Avc, 10000, [], [WorkingHost]);
		List<Exception> failures = [];
		using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

		Task<LiveStreamSession> opening = LiveStreamReader.OpenAsync(client, candidate, StreamHostSelection.FirstResponse, ConnectTimeout, (_, error) => failures.Add(error), new FakeTimeProvider(), stop.Token);
		await hung.Task.WaitAsync(cancellationToken);
		await stop.CancelAsync();
		await Assert.ThrowsAsync<OperationCanceledException>(() => opening);

		await Assert.That(failures).IsEmpty();
	}

	private sealed class StalledStream(TaskCompletionSource stalled) : Stream
	{
		public override bool CanRead => true;

		public override bool CanSeek => false;

		public override bool CanWrite => false;

		public override long Length => throw new NotSupportedException();

		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			stalled.TrySetResult();
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
