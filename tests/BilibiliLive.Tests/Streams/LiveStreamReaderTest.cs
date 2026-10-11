using BilibiliLive.Streams;
using BilibiliLive.Tests.Fakes;
using System.Text;

namespace BilibiliLive.Tests.Streams;

public class LiveStreamReaderTest
{
	private static readonly Uri BrokenHost = new("https://broken.test/live.flv?sign=1");
	private static readonly Uri WorkingHost = new("https://working.test/live.flv?sign=2");

	[Test]
	public async Task HttpStreamSkipsFailingHostAndCopiesBodyInBlocksAsync(CancellationToken cancellationToken)
	{
		byte[] body = new byte[300_000];
		new Random(42).NextBytes(body);
		FakeHttpMessageHandler handler = new(request => request.RequestUri == WorkingHost ? Responses.Bytes(body) : Responses.NotFound());
		using HttpClient client = new(handler);
		LiveStreamCandidate candidate = new(StreamProtocol.HttpStream, StreamFormat.Flv, StreamCodec.Avc, 10000, [BrokenHost, WorkingHost]);

		await using LiveStreamSession session = await LiveStreamReader.OpenAsync(client, candidate, StreamHostSelection.FirstResponse, cancellationToken);
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
			{ Host: "valid.test" } => Responses.Bytes(Encoding.ASCII.GetBytes("segment")),
			_ => Responses.NotFound()
		});
		using HttpClient client = new(handler);
		LiveStreamCandidate candidate = new(StreamProtocol.HttpHls, StreamFormat.Ts, StreamCodec.Avc, 10000, [garbage, valid]);

		await using LiveStreamSession session = await LiveStreamReader.OpenAsync(client, candidate, StreamHostSelection.FirstResponse, cancellationToken);
		using MemoryStream output = new();
		await session.CopyToAsync(output, null, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(session.Source).IsEqualTo(valid);
			await Assert.That(Encoding.ASCII.GetString(output.ToArray())).IsEqualTo("segment");
		}
	}

	[Test]
	public async Task AllHostsFailingThrowsAsync(CancellationToken cancellationToken)
	{
		using HttpClient client = new(new FakeHttpMessageHandler(_ => Responses.NotFound()));
		LiveStreamCandidate candidate = new(StreamProtocol.HttpStream, StreamFormat.Flv, StreamCodec.Avc, 10000, [BrokenHost, WorkingHost]);

		AggregateException? exception = await Assert.ThrowsAsync<AggregateException>(() => LiveStreamReader.OpenAsync(client, candidate, StreamHostSelection.FastestResponse, cancellationToken));

		await Assert.That(exception?.InnerExceptions.Count).IsEqualTo(2);
	}
}
