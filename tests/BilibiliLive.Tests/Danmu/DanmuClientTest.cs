using BilibiliLive.Danmu;
using BilibiliLive.Http;
using BilibiliLive.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace BilibiliLive.Tests.Danmu;

public class DanmuClientTest
{
	private const long RoomId = 6154037;
	private const string DefaultCookie = "SESSDATA=x";

	private static byte[] Live => DanmuPacketTest.Json("""{"cmd":"LIVE"}""");

	private sealed class Harness : IDisposable
	{
		private readonly Channel<WebSocket> _pending = Channel.CreateUnbounded<WebSocket>();
		private readonly ConcurrentQueue<WebSocket> _accepted = new();
		private readonly DanmuClient _client;

		public Harness(Func<HttpRequestMessage, HttpResponseMessage> danmuInfo, Func<HttpResponseMessage> nav, string cookie = DefaultCookie, TimeProvider? timeProvider = null)
		{
			Cookie = cookie;
			Identity = new BilibiliIdentity(_ => ValueTask.FromResult(new BilibiliRequestOptions(Cookie: Cookie)));
			Api = new ApiClient(async (request, ct) =>
			{
				if (request.Headers.Contains("Sec-WebSocket-Key"))
				{
					UpgradeUri = request.RequestUri;
					UpgradeCookie = GetCookie(request);
					(HttpResponseMessage response, WebSocket server) = await LoopbackWebSocket.AcceptAsync(request, ct);
					_accepted.Enqueue(server);
					_pending.Writer.TryWrite(server);
					return response;
				}

				return request.RequestUri?.AbsolutePath switch
				{
					ApiPaths.Nav => nav(),
					ApiPaths.DanmuInfo => danmuInfo(request),
					_ => Responses.NotFound()
				};
			}, timeProvider, Identity);
			_client = new DanmuClient(Api.Client, Identity);
		}

		public string Cookie { get; set; }

		public BilibiliIdentity Identity { get; }

		public ApiClient Api { get; }

		public DanmuReceiveOptions Options { get; } = new();

		public Uri? UpgradeUri { get; private set; }

		public string? UpgradeCookie { get; private set; }

		public IAsyncEnumerable<DanmuMessage> ReceiveAsync(CancellationToken cancellationToken)
		{
			return _client.ReceiveAsync(RoomId, Options, cancellationToken);
		}

		public Task<List<DanmuMessage>> ReceiveAllAsync(CancellationToken cancellationToken)
		{
			return ReceiveAsync(cancellationToken).ToListAsync(cancellationToken).AsTask();
		}

		public ValueTask<WebSocket> AcceptAsync(CancellationToken cancellationToken)
		{
			return _pending.Reader.ReadAsync(cancellationToken);
		}

		public int RequestCount(string path)
		{
			return Api.Handler.RequestsTo(path).Count();
		}

		public void Dispose()
		{
			Api.Dispose();

			foreach (WebSocket server in _accepted)
			{
				server.Dispose();
			}
		}
	}

	private static HttpResponseMessage RiskControl()
	{
		return Responses.Json("""{"code":-352,"message":"-352","ttl":1}""");
	}

	private static string? GetCookie(HttpRequestMessage request)
	{
		return request.Headers.TryGetValues("Cookie", out IEnumerable<string>? cookies) ? string.Join("; ", cookies) : null;
	}

	private static Harness CreateDefault(string cookie = DefaultCookie, TimeProvider? timeProvider = null)
	{
		return new Harness(static _ => Responses.Fixture("danmu_info.json"), static () => Responses.Fixture("nav_logged_in.json"), cookie, timeProvider);
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

	private static async Task ReceiveHeartbeatAsync(WebSocket server, CancellationToken cancellationToken)
	{
		DanmuPacket packet = await Assert.That(await ReceivePacketsAsync(server, cancellationToken)).HasSingleItem();
		await Assert.That(packet.Operation).IsEqualTo(DanmuOperation.Heartbeat);
	}

	private static Task SendAsync(WebSocket server, byte[] data, CancellationToken cancellationToken)
	{
		return server.SendAsync(data, WebSocketMessageType.Binary, true, cancellationToken);
	}

	private static async Task<WebSocket> AuthenticateAsync(Harness harness, string reply, CancellationToken cancellationToken)
	{
		WebSocket server = await harness.AcceptAsync(cancellationToken);
		await ReceivePacketsAsync(server, cancellationToken);
		await SendAsync(server, DanmuPacket.Encode(DanmuOperation.AuthReply, 1, Encoding.UTF8.GetBytes(reply)), cancellationToken);
		return server;
	}

	private static async Task<(JsonElement Auth, List<DanmuMessage> Messages)> AuthenticateAndCloseAsync(Harness harness, CancellationToken cancellationToken)
	{
		Task<List<DanmuMessage>> receiving = harness.ReceiveAllAsync(cancellationToken);

		WebSocket server = await harness.AcceptAsync(cancellationToken);
		DanmuPacket auth = await Assert.That(await ReceivePacketsAsync(server, cancellationToken)).HasSingleItem();
		await SendAsync(server, DanmuPacket.Encode(DanmuOperation.AuthReply, 1, """{"code":0}"""u8), cancellationToken);
		await server.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
		List<DanmuMessage> messages = await receiving;

		using JsonDocument document = JsonDocument.Parse(auth.Body);
		return (document.RootElement.Clone(), messages);
	}

	private static async Task<WebSocket> ChangeLoginAndReceiveCloseAsync(Harness harness, FakeTimeProvider time, CancellationToken cancellationToken)
	{
		WebSocket server = await AuthenticateAsync(harness, """{"code":0}""", cancellationToken);
		await ReceiveHeartbeatAsync(server, cancellationToken);
		harness.Cookie = "SESSDATA=other";
		time.Advance(DanmuClient.HeartbeatInterval);

		ValueWebSocketReceiveResult close = await server.ReceiveAsync(new byte[16].AsMemory(), cancellationToken);
		await Assert.That(close.MessageType).IsEqualTo(WebSocketMessageType.Close);
		return server;
	}

	private static DanmuMessage WithoutReceiveTime(DanmuMessage message)
	{
		return message with { ReceivedAt = default };
	}

	private static async Task<List<DanmuMessage>> TakeAsync(IAsyncEnumerator<DanmuMessage> messages, int count)
	{
		List<DanmuMessage> taken = [];

		while (taken.Count < count && await messages.MoveNextAsync())
		{
			taken.Add(messages.Current);
		}

		return taken;
	}

	[Test]
	public async Task ReceivesCommandsUntilServerClosesAsync(CancellationToken cancellationToken)
	{
		using Harness harness = CreateDefault();
		Task<List<DanmuMessage>> receiving = harness.ReceiveAllAsync(cancellationToken);

		WebSocket server = await harness.AcceptAsync(cancellationToken);
		DanmuPacket auth = await Assert.That(await ReceivePacketsAsync(server, cancellationToken)).HasSingleItem();

		await SendAsync(server, DanmuPacket.Encode(DanmuOperation.AuthReply, 1, """{"code":0}"""u8), cancellationToken);
		DanmuPacket heartbeat = await Assert.That(await ReceivePacketsAsync(server, cancellationToken)).HasSingleItem();

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
			await Assert.That(messages.Select(WithoutReceiveTime)).IsEquivalentTo([new DanmuMessage.Connected(12345678), new DanmuMessage.LiveStarted(), new DanmuMessage.LiveStopped(true), new DanmuMessage.RoomChanged("T", "A", "P")], EqualityComparer<DanmuMessage>.Default, CollectionOrdering.Matching);
			await Assert.That((auth.Operation, auth.ProtocolVersion)).IsEqualTo((DanmuOperation.Auth, (ushort)1));
			await Assert.That(Encoding.UTF8.GetString(auth.Body.Span)).IsEqualTo($$"""{"uid":12345678,"roomid":{{RoomId}},"protover":3,"platform":"web","type":2,"buvid":"{{harness.Identity.GeneratedBuvid3}}","key":"danmu-token"}""");
			await Assert.That(heartbeat.Operation).IsEqualTo(DanmuOperation.Heartbeat);
			await Assert.That(harness.UpgradeUri?.AbsolutePath).IsEqualTo("/sub");
			await Assert.That(harness.UpgradeUri?.Authority).IsEqualTo("zj-cn-live-comet.chat.bilibili.com:2245").Or.IsEqualTo("broadcastlv.chat.bilibili.com");
			await Assert.That(server.State).IsEqualTo(WebSocketState.Closed);
		}
	}

	[Test]
	public async Task RiskControlRetriesAnonymouslyAsync(CancellationToken cancellationToken)
	{
		using Harness harness = new(static request => CookieHeader.GetValue(GetCookie(request), "SESSDATA") is null ? Responses.Fixture("danmu_info.json") : RiskControl(), static () => Responses.Fixture("nav_logged_in.json"));

		(JsonElement auth, _) = await AuthenticateAndCloseAsync(harness, cancellationToken);
		string?[] cookies = [.. harness.Api.Handler.RequestsTo(ApiPaths.DanmuInfo).Select(static r => r.Headers.GetValueOrDefault("Cookie"))];

		await Assert.That(cookies).Count().IsEqualTo(2);

		using (Assert.Multiple())
		{
			await Assert.That(CookieHeader.GetValue(cookies[1], "SESSDATA")).IsNull();
			await Assert.That(CookieHeader.GetValue(cookies[1], "buvid3")).IsEqualTo(harness.Identity.GeneratedBuvid3);
			await Assert.That(auth.GetProperty("uid").GetInt64()).IsEqualTo(0);
			await Assert.That(auth.GetProperty("key").GetString()).IsEqualTo("danmu-token");
			await Assert.That(auth.GetProperty("buvid").GetString()).IsEqualTo(harness.Identity.GeneratedBuvid3);
			await AssertAnonymousHandshakeAsync(harness, auth);
		}
	}

	[Test]
	public async Task UserIdFailureRequestsAnAnonymousTokenAsync(CancellationToken cancellationToken)
	{
		int navRequests = 0;
		using Harness harness = new
		(
			static request => CookieHeader.GetValue(GetCookie(request), "SESSDATA") is null ? Responses.Fixture("danmu_info.json") : Responses.Json("""{"code":0,"data":{"token":"login-token","host_list":[{"host":"broadcastlv.chat.bilibili.com","wss_port":443}]}}"""),
			() => ++navRequests is 1 ? Responses.Status(HttpStatusCode.InternalServerError) : Responses.Fixture("nav_logged_in.json")
		);

		(JsonElement auth, _) = await AuthenticateAndCloseAsync(harness, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(harness.RequestCount(ApiPaths.DanmuInfo)).IsEqualTo(1);
			await Assert.That(auth.GetProperty("uid").GetInt64()).IsEqualTo(0);
			await Assert.That(auth.GetProperty("key").GetString()).IsEqualTo("danmu-token");
			await AssertAnonymousHandshakeAsync(harness, auth);
		}
	}

	[Test]
	public async Task RiskControlOnAnonymousRetryFallsBackToAnonymousDefaultServerAsync(CancellationToken cancellationToken)
	{
		using Harness harness = new(static _ => RiskControl(), static () => Responses.Fixture("nav_logged_in.json"));
		await AssertAnonymousDefaultServerAsync(harness, cancellationToken);
	}

	[Test]
	public async Task ApiFailureFallsBackToAnonymousDefaultServerAsync(CancellationToken cancellationToken)
	{
		using Harness harness = new(static _ => Responses.Fixture("danmu_info.json"), static () => Responses.Status(HttpStatusCode.InternalServerError));
		await AssertAnonymousDefaultServerAsync(harness, cancellationToken);
	}

	private static async Task AssertAnonymousDefaultServerAsync(Harness harness, CancellationToken cancellationToken)
	{
		(JsonElement auth, List<DanmuMessage> messages) = await AuthenticateAndCloseAsync(harness, cancellationToken);
		DanmuMessage connected = await Assert.That(messages).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(harness.UpgradeUri?.Host).IsEqualTo(DanmuClient.DefaultHost);
			await Assert.That(harness.UpgradeUri?.Port).IsEqualTo(DanmuClient.DefaultPort);
			await Assert.That(auth.GetProperty("uid").GetInt64()).IsEqualTo(0);
			await Assert.That(auth.TryGetProperty("key", out _)).IsFalse();
			await Assert.That(connected).IsTypeOf<DanmuMessage.Connected>().And.Member(static c => c.IsAnonymous, static anonymous => anonymous.IsTrue());
			await AssertAnonymousHandshakeAsync(harness, auth);
		}
	}

	private static async Task AssertAnonymousHandshakeAsync(Harness harness, JsonElement auth)
	{
		await Assert.That(CookieHeader.GetValue(harness.UpgradeCookie, "SESSDATA")).IsNull();
		await Assert.That(CookieHeader.GetValue(harness.UpgradeCookie, "buvid3")).IsEqualTo(auth.GetProperty("buvid").GetString());
	}

	[Test]
	public async Task ReconnectWithinCacheDurationReusesServerInfoAsync(CancellationToken cancellationToken)
	{
		FakeTimeProvider time = new();
		using Harness harness = CreateDefault(timeProvider: time);

		await AuthenticateAndCloseAsync(harness, cancellationToken);
		int navRequests = harness.RequestCount(ApiPaths.Nav);
		time.Advance(DanmuClient.ServerCacheDuration - TimeSpan.FromSeconds(1));
		(JsonElement auth, _) = await AuthenticateAndCloseAsync(harness, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(harness.RequestCount(ApiPaths.DanmuInfo)).IsEqualTo(1);
			await Assert.That(harness.RequestCount(ApiPaths.Nav)).IsEqualTo(navRequests);
			await Assert.That(auth.GetProperty("uid").GetInt64()).IsEqualTo(12345678);
			await Assert.That(auth.GetProperty("key").GetString()).IsEqualTo("danmu-token");
		}
	}

	[Test]
	public async Task ReconnectAfterCacheDurationRefetchesServerInfoAsync(CancellationToken cancellationToken)
	{
		FakeTimeProvider time = new();
		using Harness harness = CreateDefault(timeProvider: time);

		await AuthenticateAndCloseAsync(harness, cancellationToken);
		time.Advance(DanmuClient.ServerCacheDuration);
		await AuthenticateAndCloseAsync(harness, cancellationToken);

		await Assert.That(harness.RequestCount(ApiPaths.DanmuInfo)).IsEqualTo(2);
	}

	[Test]
	public async Task ChangedLoginRefetchesServerInfoWithinCacheDurationAsync(CancellationToken cancellationToken)
	{
		FakeTimeProvider time = new();
		using Harness harness = CreateDefault("buvid3=ANON", time);

		await AuthenticateAndCloseAsync(harness, cancellationToken);
		harness.Cookie = "SESSDATA=x; buvid3=ANON";
		await AuthenticateAndCloseAsync(harness, cancellationToken);
		await AuthenticateAndCloseAsync(harness, cancellationToken);

		await Assert.That(harness.RequestCount(ApiPaths.DanmuInfo)).IsEqualTo(2);
	}

	[Test]
	public async Task ChangedLoginClosesTheConnectionAtTheNextHeartbeatAsync(CancellationToken cancellationToken)
	{
		FakeTimeProvider time = new();
		using Harness harness = CreateDefault(timeProvider: time);
		Task<List<DanmuMessage>> receiving = harness.ReceiveAllAsync(cancellationToken);

		WebSocket server = await ChangeLoginAndReceiveCloseAsync(harness, time, cancellationToken);
		await server.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
		List<DanmuMessage> messages = await receiving;

		using (Assert.Multiple())
		{
			await Assert.That(server.CloseStatus).IsEqualTo(WebSocketCloseStatus.NormalClosure);
			await Assert.That(messages.Select(WithoutReceiveTime)).IsEquivalentTo([new DanmuMessage.Connected(12345678)], EqualityComparer<DanmuMessage>.Default, CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task ChangedLoginEndsNormallyWhenTheServerDropsTheConnectionInsteadOfClosingAsync(CancellationToken cancellationToken)
	{
		FakeTimeProvider time = new();
		using Harness harness = CreateDefault(timeProvider: time);
		Task<List<DanmuMessage>> receiving = harness.ReceiveAllAsync(cancellationToken);

		WebSocket server = await ChangeLoginAndReceiveCloseAsync(harness, time, cancellationToken);
		server.Abort();
		List<DanmuMessage> messages = await receiving;

		await Assert.That(messages.Select(WithoutReceiveTime)).IsEquivalentTo([new DanmuMessage.Connected(12345678)], EqualityComparer<DanmuMessage>.Default, CollectionOrdering.Matching);
	}

	[Test]
	[Arguments("SESSDATA=renewed; bili_jct=renewed; DedeUserID=12345678", WebSocketMessageType.Binary)]
	[Arguments("SESSDATA=renewed; bili_jct=renewed; DedeUserID=1", WebSocketMessageType.Close)]
	public async Task ChangedCookieReconnectsOnlyForAnotherLoginAsync(string renewedCookie, WebSocketMessageType expected, CancellationToken cancellationToken)
	{
		FakeTimeProvider time = new();
		using Harness harness = CreateDefault("SESSDATA=x; bili_jct=x; DedeUserID=12345678", time);
		Task<List<DanmuMessage>> receiving = harness.ReceiveAllAsync(cancellationToken);

		WebSocket server = await AuthenticateAsync(harness, """{"code":0}""", cancellationToken);
		await ReceiveHeartbeatAsync(server, cancellationToken);
		harness.Cookie = renewedCookie;
		time.Advance(DanmuClient.HeartbeatInterval);
		ValueWebSocketReceiveResult next = await server.ReceiveAsync(new byte[64].AsMemory(), cancellationToken);
		await server.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
		await receiving;

		await Assert.That(next.MessageType).IsEqualTo(expected);
	}

	[Test]
	public async Task AuthFailureThrowsAndDiscardsCachedServerInfoAsync(CancellationToken cancellationToken)
	{
		using Harness harness = CreateDefault();
		Task<List<DanmuMessage>> rejected = harness.ReceiveAllAsync(cancellationToken);
		await AuthenticateAsync(harness, """{"code":-101}""", cancellationToken);
		await Assert.ThrowsAsync<BilibiliApiException>(() => rejected).And.Member(static e => e.Code, static code => code.IsEqualTo(-101));

		await AuthenticateAndCloseAsync(harness, cancellationToken);

		await Assert.That(harness.RequestCount(ApiPaths.DanmuInfo)).IsEqualTo(2);
	}

	[Test]
	public async Task CancellationEndsEnumerationWithoutExceptionAsync(CancellationToken cancellationToken)
	{
		using Harness harness = CreateDefault();
		using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		await using IAsyncEnumerator<DanmuMessage> enumerator = harness.ReceiveAsync(cts.Token).GetAsyncEnumerator(cts.Token);
		ValueTask<bool> first = enumerator.MoveNextAsync();

		await AuthenticateAsync(harness, """{"code":0}""", cancellationToken);

		bool hasFirst = await first;
		DanmuMessage firstMessage = enumerator.Current;
		ValueTask<bool> second = enumerator.MoveNextAsync();
		await cts.CancelAsync();
		bool hasSecond = await second;

		using (Assert.Multiple())
		{
			await Assert.That((hasFirst, hasSecond)).IsEqualTo((true, false));
			await Assert.That(firstMessage).IsTypeOf<DanmuMessage.Connected>();
		}
	}

	[Test]
	public async Task ConnectionLossThrowsAsync(CancellationToken cancellationToken)
	{
		using Harness harness = CreateDefault();
		Task<List<DanmuMessage>> receiving = harness.ReceiveAllAsync(cancellationToken);

		WebSocket server = await AuthenticateAsync(harness, """{"code":0}""", cancellationToken);
		server.Abort();

		await Assert.ThrowsAsync<WebSocketException>(() => receiving);
	}

	[Test]
	public async Task OversizedMessageThrowsAsync(CancellationToken cancellationToken)
	{
		using Harness harness = CreateDefault();
		Task<List<DanmuMessage>> receiving = harness.ReceiveAllAsync(cancellationToken);

		WebSocket server = await AuthenticateAsync(harness, """{"code":0}""", cancellationToken);
		Task sending = SendAndCloseAsync();

		await Assert.ThrowsAsync<InvalidDataException>(() => receiving);
		// 客户端在消息中途断开，服务器这一端的发送可能失败
		await sending.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

		async Task SendAndCloseAsync()
		{
			byte[] packet = DanmuPacket.Encode(DanmuOperation.HeartbeatReply, 1, new byte[DanmuPacket.MaxPacketLength / 2]);

			for (int sent = 0; sent <= DanmuClient.MaxMessageLength; sent += packet.Length)
			{
				await server.SendAsync(packet, WebSocketMessageType.Binary, false, cancellationToken);
			}

			await server.SendAsync(ReadOnlyMemory<byte>.Empty, WebSocketMessageType.Binary, true, cancellationToken);
			await server.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
		}
	}

	[Test]
	[Arguments("SESSDATA=x; buvid3=MINE", "MINE")]
	public async Task AuthCarriesTheBuvid3OfTheHandshakeCookieAsync(string cookie, string? userBuvid3, CancellationToken cancellationToken)
	{
		using Harness harness = CreateDefault(cookie);
		(JsonElement auth, _) = await AuthenticateAndCloseAsync(harness, cancellationToken);

		string expected = userBuvid3 ?? harness.Identity.GeneratedBuvid3;

		using (Assert.Multiple())
		{
			await Assert.That(auth.GetProperty("buvid").GetString()).IsEqualTo(expected);
			await Assert.That(CookieHeader.GetValue(harness.UpgradeCookie, "buvid3")).IsEqualTo(expected);
			await Assert.That(CookieHeader.GetValue(harness.UpgradeCookie, "SESSDATA")).IsEqualTo("x");
		}
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task SilentConnectionIsAbortedAfterReceiveTimeoutAsync(bool receiving, CancellationToken cancellationToken)
	{
		FakeTimeProvider time = new();
		using Harness harness = CreateDefault(timeProvider: time);
		await using IAsyncEnumerator<DanmuMessage> messages = harness.ReceiveAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
		Task<bool> next = messages.MoveNextAsync().AsTask();

		WebSocket server = await AuthenticateAsync(harness, """{"code":0}""", cancellationToken);
		await Assert.That(await next).IsTrue();
		await ReceiveHeartbeatAsync(server, cancellationToken);

		if (receiving)
		{
			next = messages.MoveNextAsync().AsTask();
		}

		for (TimeSpan idle = DanmuClient.HeartbeatInterval; idle <= DanmuClient.ReceiveTimeout; idle += DanmuClient.HeartbeatInterval)
		{
			time.Advance(DanmuClient.HeartbeatInterval);
			await ReceiveHeartbeatAsync(server, cancellationToken);
		}

		time.Advance(DanmuClient.HeartbeatInterval);

		if (!receiving)
		{
			await Assert.ThrowsAsync<WebSocketException>(async () => await server.ReceiveAsync(new byte[16].AsMemory(), cancellationToken));
			next = messages.MoveNextAsync().AsTask();
		}

		await Assert.ThrowsAsync<TimeoutException>(async () => await next);
	}

	[Test]
	public async Task IncomingPacketsKeepTheConnectionAliveAsync(CancellationToken cancellationToken)
	{
		FakeTimeProvider time = new();
		using Harness harness = CreateDefault(timeProvider: time);
		await using IAsyncEnumerator<DanmuMessage> messages = harness.ReceiveAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
		Task<bool> next = messages.MoveNextAsync().AsTask();

		WebSocket server = await AuthenticateAsync(harness, """{"code":0}""", cancellationToken);
		await Assert.That(await next).IsTrue();
		next = messages.MoveNextAsync().AsTask();
		await ReceiveHeartbeatAsync(server, cancellationToken);

		for (TimeSpan elapsed = TimeSpan.Zero; elapsed <= DanmuClient.ReceiveTimeout * 2; elapsed += DanmuClient.HeartbeatInterval)
		{
			await SendAsync(server, Live, cancellationToken);
			await Assert.That(await next).IsTrue();
			next = messages.MoveNextAsync().AsTask();

			time.Advance(DanmuClient.HeartbeatInterval);
			await ReceiveHeartbeatAsync(server, cancellationToken);
		}

		await server.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);

		await Assert.That(await next).IsFalse();
	}

	[Test]
	public async Task OptionsTakeEffectWithoutReconnectingAsync(CancellationToken cancellationToken)
	{
		FakeTimeProvider time = new();
		using Harness harness = CreateDefault(timeProvider: time);
		await using IAsyncEnumerator<DanmuMessage> messages = harness.ReceiveAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
		ValueTask<bool> connected = messages.MoveNextAsync();
		WebSocket server = await AuthenticateAsync(harness, """{"code":0}""", cancellationToken);
		await Assert.That(await connected).IsTrue();

		byte[] comment = DanmuMessageParserTest.Fixture("comment_masked.json");
		byte[] frame = DanmuPacketTest.Brotli(DanmuPacket.Encode(DanmuOperation.Message, 0, comment), Live);

		await SendAsync(server, frame, cancellationToken);
		List<DanmuMessage> plain = await TakeAsync(messages, 1);

		harness.Options.IncludeChat = true;
		harness.Options.IncludeRaw = true;
		time.Advance(TimeSpan.FromSeconds(1));
		DanmuReceiveTime frameTime = new(time.GetTimestamp(), time.GetUtcNow());
		await SendAsync(server, frame, cancellationToken);
		List<DanmuMessage> full = await TakeAsync(messages, 4);

		harness.Options.IncludeChat = false;
		harness.Options.IncludeRaw = false;
		await SendAsync(server, frame, cancellationToken);
		List<DanmuMessage> plainAgain = await TakeAsync(messages, 1);

		Task<bool> next = messages.MoveNextAsync().AsTask();
		await server.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
		bool hasMore = await next;

		await Assert.That(full).Count().IsEqualTo(4);

		using (Assert.Multiple())
		{
			await Assert.That(plain.Select(WithoutReceiveTime)).IsEquivalentTo([new DanmuMessage.LiveStarted()], EqualityComparer<DanmuMessage>.Default, CollectionOrdering.Matching);
			await Assert.That(full[0]).IsTypeOf<DanmuMessage.Raw>()
				.And.Member(static raw => raw.Command, static command => command.IsEqualTo("DANMU_MSG"))
				.And.Member(static raw => Encoding.UTF8.GetString(raw.Json.ToArray()), json => json.IsEqualTo(Encoding.UTF8.GetString(comment)));
			await Assert.That(full[1]).IsTypeOf<DanmuMessage.Comment>().And.Member(static c => c.Text, static text => text.IsEqualTo("666"));
			await Assert.That(full[2]).IsTypeOf<DanmuMessage.Raw>().And.Member(static raw => raw.Command, static command => command.IsEqualTo("LIVE"));
			await Assert.That(full[3]).IsTypeOf<DanmuMessage.LiveStarted>();
			await Assert.That(full).All(m => m.ReceivedAt == frameTime);
			await Assert.That(plainAgain.Select(WithoutReceiveTime)).IsEquivalentTo([new DanmuMessage.LiveStarted()], EqualityComparer<DanmuMessage>.Default, CollectionOrdering.Matching);
			await Assert.That(hasMore).IsFalse();
		}
	}

	[Test]
	public async Task MalformedChatDoesNotInterruptReceivingAsync(CancellationToken cancellationToken)
	{
		FakeTimeProvider time = new();
		using Harness harness = CreateDefault(timeProvider: time);
		harness.Options.IncludeChat = true;
		Task<List<DanmuMessage>> receiving = harness.ReceiveAllAsync(cancellationToken);

		WebSocket server = await AuthenticateAsync(harness, """{"code":0}""", cancellationToken);
		await SendAsync(server, DanmuPacketTest.Brotli
		(
			DanmuPacketTest.Json("""{"cmd":"SEND_GIFT_V2","data":{"pb":"not base64"}}"""),
			// 礼物字段的长度超出末尾
			DanmuPacketTest.Json("""{"cmd":"SEND_GIFT_V2","data":{"pb":"UgU="}}"""),
			DanmuPacketTest.Json("""{"cmd":"DANMU_MSG","info":[[0,1"""),
			DanmuPacketTest.Json("""{"cmd":"DANMU_MSG","info":[[0,1,25],"\ud83d"]}"""),
			DanmuPacketTest.Json("""{"cmd":"SUPER_CHAT_MESSAGE","data":{"price":30,"time":1e30}}"""),
			Live
		), cancellationToken);
		await server.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);

		List<DanmuMessage> messages = await receiving;

		DanmuMessage.SuperChat superChat = new(0, new DanmuSender(0, string.Empty, GuardLevel.None, null), string.Empty, 30_000, TimeSpan.Zero);
		await Assert.That(messages.Select(WithoutReceiveTime)).IsEquivalentTo([new DanmuMessage.Connected(12345678), superChat, new DanmuMessage.LiveStarted()], EqualityComparer<DanmuMessage>.Default, CollectionOrdering.Matching);
	}
}
