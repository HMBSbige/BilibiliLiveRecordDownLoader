using BilibiliLive.Danmu;
using BilibiliLive.Tests.Fakes;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace BilibiliLive.Tests.Danmu;

public class DanmuClientTest
{
	private const long RoomId = 6154037;

	private sealed class Harness : IDisposable
	{
		public Harness(Func<HttpResponseMessage> danmuInfo, Func<HttpResponseMessage> nav)
		{
			Api = new ApiClient(async (request, ct) =>
			{
				if (request.Headers.Contains("Sec-WebSocket-Key"))
				{
					UpgradeUri = request.RequestUri;
					(HttpResponseMessage response, WebSocket server) = await LoopbackWebSocket.AcceptAsync(request, ct);
					Server.SetResult(server);
					return response;
				}

				return request.RequestUri?.AbsolutePath switch
				{
					ApiPaths.Nav => nav(),
					ApiPaths.DanmuInfo => danmuInfo(),
					_ => Responses.NotFound()
				};
			});
			Client = new DanmuClient(Api.Client);
		}

		public ApiClient Api { get; }

		public DanmuClient Client { get; }

		public TaskCompletionSource<WebSocket> Server { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Uri? UpgradeUri { get; private set; }

		public void Dispose()
		{
			Api.Dispose();

			if (Server.Task.IsCompletedSuccessfully)
			{
				Server.Task.Result.Dispose();
			}
		}
	}

	private static Harness CreateDefault()
	{
		return new Harness(() => Responses.Fixture("danmu_info.json"), () => Responses.Fixture("nav_logged_in.json"));
	}

	private static async Task<List<DanmuPacket>> ReceivePacketsAsync(WebSocket server, CancellationToken cancellationToken)
	{
		byte[] buffer = new byte[64 * 1024];
		int length = 0;
		ValueWebSocketReceiveResult result;

		do
		{
			result = await server.ReceiveAsync(buffer.AsMemory(length), cancellationToken);
			length += result.Count;
		}
		while (!result.EndOfMessage);

		return DanmuPacket.Decode(buffer.AsMemory(0, length));
	}

	private static async Task SendAsync(WebSocket server, byte[] data, CancellationToken cancellationToken)
	{
		await server.SendAsync(data, WebSocketMessageType.Binary, true, cancellationToken);
	}

	private static async Task<WebSocket> AuthenticateAsync(Harness harness, string reply, CancellationToken cancellationToken)
	{
		WebSocket server = await harness.Server.Task.WaitAsync(cancellationToken);
		await ReceivePacketsAsync(server, cancellationToken);
		await SendAsync(server, DanmuPacket.Encode(DanmuOperation.AuthReply, 1, Encoding.UTF8.GetBytes(reply)), cancellationToken);
		return server;
	}

	private static async Task<List<DanmuMessage>> CollectAsync(IAsyncEnumerable<DanmuMessage> source)
	{
		List<DanmuMessage> messages = [];

		await foreach (DanmuMessage message in source)
		{
			messages.Add(message);
		}

		return messages;
	}

	[Test]
	[Timeout(10_000)]
	public async Task ReceivesCommandsUntilServerClosesAsync(CancellationToken cancellationToken)
	{
		using Harness harness = CreateDefault();
		Task<List<DanmuMessage>> receiving = CollectAsync(harness.Client.ReceiveAsync(RoomId, cancellationToken));

		WebSocket server = await harness.Server.Task.WaitAsync(cancellationToken);
		DanmuPacket auth = (await ReceivePacketsAsync(server, cancellationToken)).Single();
		using JsonDocument authBody = JsonDocument.Parse(auth.Body);
		JsonElement authJson = authBody.RootElement;

		await SendAsync(server, DanmuPacket.Encode(DanmuOperation.AuthReply, 1, """{"code":0}"""u8), cancellationToken);
		DanmuPacket heartbeat = (await ReceivePacketsAsync(server, cancellationToken)).Single();

		await SendAsync(server, DanmuPacketTest.Brotli(
			DanmuPacketTest.Json("""{"cmd":"DANMU_MSG:4:0:2:2:2:0","info":[]}"""),
			DanmuPacketTest.Json("""{"cmd":"LIVE","roomid":6154037}"""),
			DanmuPacketTest.Json("""{"cmd":"PREPARING","round":1,"roomid":"6154037"}""")), cancellationToken);
		await SendAsync(server, DanmuPacket.Encode(DanmuOperation.HeartbeatReply, 1, [0, 0, 0, 1]), cancellationToken);
		await SendAsync(server, DanmuPacketTest.Zlib(DanmuPacketTest.Json("""{"cmd":"ROOM_CHANGE","data":{"title":"T","area_name":"A","parent_area_name":"P"}}""")), cancellationToken);
		await server.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", cancellationToken);

		List<DanmuMessage> messages = await receiving;

		using (Assert.Multiple())
		{
			await Sequences.AssertEqualAsync(messages, [new DanmuMessage.LiveStarted(), new DanmuMessage.LiveStopped(true), new DanmuMessage.RoomChanged("T", "A", "P")]);
			await Assert.That((auth.Operation, auth.ProtocolVersion)).IsEqualTo((DanmuOperation.Auth, (ushort)1));
			await Assert.That(authJson.GetProperty("uid").GetInt64()).IsEqualTo(12345678);
			await Assert.That(authJson.GetProperty("roomid").GetInt64()).IsEqualTo(RoomId);
			await Assert.That(authJson.GetProperty("protover").GetInt32()).IsEqualTo(3);
			await Assert.That(authJson.GetProperty("key").GetString()).IsEqualTo("danmu-token");
			await Assert.That(heartbeat.Operation).IsEqualTo(DanmuOperation.Heartbeat);
			await Assert.That(harness.UpgradeUri?.AbsolutePath).IsEqualTo("/sub");
			await Assert.That(harness.UpgradeUri?.Authority is "zj-cn-live-comet.chat.bilibili.com:2245" or "broadcastlv.chat.bilibili.com").IsTrue();
			await Assert.That(server.State).IsEqualTo(WebSocketState.Closed);
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task AuthFailureThrowsAsync(CancellationToken cancellationToken)
	{
		using Harness harness = CreateDefault();
		Task<List<DanmuMessage>> receiving = CollectAsync(harness.Client.ReceiveAsync(RoomId, cancellationToken));

		await AuthenticateAsync(harness, """{"code":-101}""", cancellationToken);

		BilibiliApiException? exception = await Assert.ThrowsAsync<BilibiliApiException>(() => receiving);

		await Assert.That(exception?.Code).IsEqualTo(-101);
	}

	[Test]
	[Timeout(10_000)]
	public async Task RiskControlFallsBackToAnonymousDefaultServerAsync(CancellationToken cancellationToken)
	{
		using Harness harness = new(() => Responses.Json("""{"code":-352,"message":"-352","ttl":1}"""), () => Responses.Fixture("nav_logged_in.json"));
		await AssertAnonymousDefaultServerAsync(harness, cancellationToken);
	}

	[Test]
	[Timeout(10_000)]
	public async Task ApiFailureFallsBackToAnonymousDefaultServerAsync(CancellationToken cancellationToken)
	{
		using Harness harness = new(() => Responses.Fixture("danmu_info.json"), () => Responses.Status(HttpStatusCode.InternalServerError));
		await AssertAnonymousDefaultServerAsync(harness, cancellationToken);
	}

	private static async Task AssertAnonymousDefaultServerAsync(Harness harness, CancellationToken cancellationToken)
	{
		Task<List<DanmuMessage>> receiving = CollectAsync(harness.Client.ReceiveAsync(RoomId, cancellationToken));

		WebSocket server = await harness.Server.Task.WaitAsync(cancellationToken);
		DanmuPacket auth = (await ReceivePacketsAsync(server, cancellationToken)).Single();
		using JsonDocument authBody = JsonDocument.Parse(auth.Body);
		await server.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
		await receiving;

		using (Assert.Multiple())
		{
			await Assert.That(harness.UpgradeUri?.Host).IsEqualTo(DanmuClient.DefaultHost);
			await Assert.That(harness.UpgradeUri?.Port).IsEqualTo(DanmuClient.DefaultPort);
			await Assert.That(authBody.RootElement.GetProperty("uid").GetInt64()).IsEqualTo(0);
			await Assert.That(authBody.RootElement.TryGetProperty("key", out _)).IsFalse();
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task CancellationEndsEnumerationWithoutExceptionAsync(CancellationToken cancellationToken)
	{
		using Harness harness = CreateDefault();
		using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		await using IAsyncEnumerator<DanmuMessage> enumerator = harness.Client.ReceiveAsync(RoomId, cts.Token).GetAsyncEnumerator(cts.Token);
		ValueTask<bool> first = enumerator.MoveNextAsync();

		WebSocket server = await AuthenticateAsync(harness, """{"code":0}""", cancellationToken);
		await SendAsync(server, DanmuPacketTest.Json("""{"cmd":"LIVE"}"""), cancellationToken);

		bool hasFirst = await first;
		DanmuMessage firstMessage = enumerator.Current;
		ValueTask<bool> second = enumerator.MoveNextAsync();
		await cts.CancelAsync();
		bool hasSecond = await second;

		using (Assert.Multiple())
		{
			await Assert.That(hasFirst).IsTrue();
			await Assert.That(firstMessage).IsEqualTo(new DanmuMessage.LiveStarted());
			await Assert.That(hasSecond).IsFalse();
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task ConnectionLossThrowsAsync(CancellationToken cancellationToken)
	{
		using Harness harness = CreateDefault();
		Task<List<DanmuMessage>> receiving = CollectAsync(harness.Client.ReceiveAsync(RoomId, cancellationToken));

		WebSocket server = await AuthenticateAsync(harness, """{"code":0}""", cancellationToken);
		server.Abort();

		await Assert.ThrowsAsync<WebSocketException>(() => receiving);
	}
}
