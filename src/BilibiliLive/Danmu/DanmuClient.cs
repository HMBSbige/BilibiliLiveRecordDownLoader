using BilibiliLive.Api;
using BilibiliLive.Http;
using CryptoBase.Hashes;
using CryptoBase.Hashes.Sha2;
using CryptoBase.Platform;
using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace BilibiliLive.Danmu;

public sealed partial class DanmuClient(BilibiliApiClient apiClient, BilibiliIdentity identity, ILogger<DanmuClient>? logger = null)
{
	internal const string DefaultHost = "broadcastlv.chat.bilibili.com";
	internal const ushort DefaultPort = 443;
	internal const int MaxMessageLength = 8 * 1024 * 1024;

	private const ushort SendProtocolVersion = 1;
	private const int ReceiveChunkSize = 16 * 1024;
	private const string UserIdCookieName = "DedeUserID";

	internal static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

	// 服务器每个心跳都会回复，这么久没有任何数据说明连接已经半开
	internal static readonly TimeSpan ReceiveTimeout = HeartbeatInterval * 2.5;
	private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(5);

	// 房间断线几秒内就会重连，每次都重新获取会触发风控
	internal static readonly TimeSpan ServerCacheDuration = TimeSpan.FromMinutes(1);

	private readonly ILogger _logger = logger ?? NullLogger<DanmuClient>.Instance;
	private readonly ConcurrentDictionary<long, DanmuServer> _servers = new();

	public async IAsyncEnumerable<DanmuMessage> ReceiveAsync(long roomId, DanmuReceiveOptions options, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		await using IAsyncEnumerator<DanmuMessage> enumerator = ReceiveCoreAsync(roomId, options, cancellationToken).GetAsyncEnumerator(cancellationToken);

		while (true)
		{
			try
			{
				if (!await enumerator.MoveNextAsync())
				{
					break;
				}
			}
			// 取消挂起的 WebSocket 操作会中止 socket，表现为各种类型的异常
			catch (Exception) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}

			yield return enumerator.Current;
		}
	}

	private async IAsyncEnumerable<DanmuMessage> ReceiveCoreAsync(long roomId, DanmuReceiveOptions options, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		DanmuEndpoint endpoint = await ResolveEndpointAsync(roomId, cancellationToken);
		Uri uri = new($"wss://{endpoint.Host}:{endpoint.Port}/sub");

		LogConnecting(roomId, uri);

		using ClientWebSocket socket = new();

		// uid 0 的 token 是用匿名 cookie 申请的，握手必须和认证包呈现同一个身份
		if (endpoint.UserId is 0)
		{
			socket.Options.SetRequestHeader(BilibiliRequestHandler.AnonymousHeader, "1");
		}

		// 经由 API 的 HttpClient 握手，WebSocket 的代理、User-Agent 和 buvid3 都和 API 请求一致
		await socket.ConnectAsync(uri, apiClient.HttpClient, cancellationToken);

		string buvid3 = await identity.GetBuvid3Async(cancellationToken);
		DanmuAuthBody auth = new(endpoint.UserId, roomId, buvid3, endpoint.Token);
		await SendAsync(socket, DanmuOperation.Auth, JsonSerializer.SerializeToUtf8Bytes(auth, BilibiliJsonContext.Default.DanmuAuthBody), cancellationToken);

		using CancellationTokenSource heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		ReceiveActivity activity = new(apiClient.TimeProvider);
		Task heartbeat = HeartbeatAsync(socket, activity, endpoint.UserId, buvid3, endpoint.IdentityHash, heartbeatCts.Token);
		ArrayBufferWriter<byte> buffer = new(ReceiveChunkSize);
		using DanmuPacketDecoder decoder = new();
		List<DanmuMessage> messages = [];

		try
		{
			while (await ReceiveMessageAsync(socket, buffer, activity, cancellationToken))
			{
				activity.Touch();

				// 整帧收完才取时间，服务器批量下发的一帧不会被分成几个到达时间
				DanmuReceiveTime receivedAt = new(apiClient.TimeProvider.GetTimestamp(), apiClient.TimeProvider.GetUtcNow());
				DanmuParseMode mode = options.Mode;

				foreach (DanmuPacket packet in decoder.Decode(buffer.WrittenMemory))
				{
					switch (packet.Operation)
					{
						case DanmuOperation.AuthReply:
						{
							EnsureAuthenticated(roomId, packet.Body);
							yield return new DanmuMessage.Connected(endpoint.UserId) { ReceivedAt = receivedAt };
							break;
						}
						case DanmuOperation.Message:
						{
							ParseOrSkip(packet.Body, mode, receivedAt, messages);

							foreach (DanmuMessage message in messages)
							{
								yield return message;
							}

							messages.Clear();
							break;
						}
					}
				}

				buffer.ResetWrittenCount();
			}

			if (activity.End is not ConnectionEnd.IdentityChanged)
			{
				LogServerClosed(roomId, socket.CloseStatus, socket.CloseStatusDescription);
			}
		}
		finally
		{
			await heartbeatCts.CancelAsync();
			await heartbeat;
			await CloseOutputAsync(socket);
		}
	}

	private async Task<DanmuEndpoint> ResolveEndpointAsync(long roomId, CancellationToken cancellationToken)
	{
		// 在请求之前取，请求期间登录只会多一次重连，不会把旧身份的 token 当成新身份的用
		string identityHash = await GetIdentityHashAsync(cancellationToken);

		// token 只能认证签发它时的 uid
		if (!_servers.TryGetValue(roomId, out DanmuServer? server) || apiClient.TimeProvider.GetElapsedTime(server.FetchedAt) >= ServerCacheDuration || server.IdentityHash != identityHash)
		{
			server = await FetchServerAsync(roomId, identityHash, cancellationToken);

			if (server is null)
			{
				_servers.TryRemove(roomId, out _);

				return new DanmuEndpoint(DefaultHost, DefaultPort, null, 0, identityHash);
			}

			_servers[roomId] = server;
		}

		// 随机挑选主机，调用方重连时就能避开坏掉的那台
		DanmuHost host = server.Info.Hosts[Random.Shared.Next(server.Info.Hosts.Count)];
		return new DanmuEndpoint(host.Host, host.WssPort, server.Info.Token, server.UserId, identityHash);
	}

	// 只保留哈希，记录缓存的服务器信息时不会泄露 cookie
	private async ValueTask<string> GetIdentityHashAsync(CancellationToken cancellationToken)
	{
		string cookie = await identity.GetCookieAsync(cancellationToken);
		byte[] buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(cookie.Length));

		try
		{
			ReadOnlySpan<byte> utf8 = buffer.AsSpan(0, Encoding.UTF8.GetBytes(cookie, buffer));
			Span<byte> hash = stackalloc byte[PlatformSha256.HashLength];

			if (PlatformSha256.IsPreferredFor(utf8.Length))
			{
				PlatformSha256.HashData(utf8, hash);
			}
			else
			{
				HashAlgorithm<Sha256Core>.HashData(utf8, hash);
			}

			return Convert.ToHexString(hash);
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer, true);
		}
	}

	private async Task<DanmuServer?> FetchServerAsync(long roomId, string identityHash, CancellationToken cancellationToken)
	{
		long userId;

		try
		{
			userId = await apiClient.GetCurrentUserIdAsync(cancellationToken);
		}
		catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
		{
			// 用登录 cookie 申请的 token 和 uid 0 对不上
			LogUserIdFailed(ex);
			return await FetchAnonymousServerAsync(roomId, identityHash, cancellationToken);
		}

		try
		{
			return ToServer(roomId, await apiClient.GetDanmuServerInfoAsync(roomId, anonymous: false, cancellationToken), userId, identityHash);
		}
		// 只有账号被风控才值得匿名重试；-412 和 HTTP 412 封的是 IP，匿名请求也一样
		catch (BilibiliApiException ex) when (ex.Code is BilibiliApiException.RiskControlCode)
		{
			LogRiskControl(roomId);
			return await FetchAnonymousServerAsync(roomId, identityHash, cancellationToken);
		}
		catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
		{
			LogServerInfoFailed(ex, roomId);
			return null;
		}
	}

	private async Task<DanmuServer?> FetchAnonymousServerAsync(long roomId, string identityHash, CancellationToken cancellationToken)
	{
		try
		{
			return ToServer(roomId, await apiClient.GetDanmuServerInfoAsync(roomId, anonymous: true, cancellationToken), 0, identityHash);
		}
		catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
		{
			LogServerInfoFailed(ex, roomId);
			return null;
		}
	}

	private DanmuServer? ToServer(long roomId, DanmuServerInfo info, long userId, string identityHash)
	{
		if (info is { Token.Length: > 0, Hosts.Count: > 0 })
		{
			return new DanmuServer(info, userId, apiClient.TimeProvider.GetTimestamp(), identityHash);
		}

		LogNoServer(roomId);
		return null;
	}

	private void EnsureAuthenticated(long roomId, ReadOnlyMemory<byte> body)
	{
		long? code = DanmuMessageParser.GetAuthReplyCode(body);

		if (code is not 0)
		{
			_servers.TryRemove(roomId, out _);
			throw new BilibiliApiException($"Danmu authentication failed: {Encoding.UTF8.GetString(body.Span)}", code ?? 0);
		}

		LogAuthenticated(roomId);
	}

	private void ParseOrSkip(ReadOnlyMemory<byte> body, DanmuParseMode mode, DanmuReceiveTime receivedAt, List<DanmuMessage> output)
	{
		try
		{
			DanmuMessageParser.Parse(body, mode, receivedAt, output);
		}
		catch (Exception ex) when (ex is JsonException or InvalidDataException or FormatException or InvalidOperationException)
		{
			LogMalformedMessage(ex);
		}
	}

	private async Task HeartbeatAsync(ClientWebSocket socket, ReceiveActivity activity, long userId, string buvid3, string identityHash, CancellationToken cancellationToken)
	{
		try
		{
			using PeriodicTimer timer = new(HeartbeatInterval, apiClient.TimeProvider);

			do
			{
				// 往断掉的 TCP 连接里发送照样成功，只有收不到回复才能发现
				TimeSpan idle = activity.Idle;

				if (idle > ReceiveTimeout)
				{
					LogReceiveTimedOut(idle);
					activity.End = new ConnectionEnd.TimedOut();
					socket.Abort();
					return;
				}

				// 连接的身份由认证包决定，登录或退出只能在新连接上生效
				if (await GetIdentityHashAsync(cancellationToken) is var current && current != identityHash)
				{
					// 续期只换 SESSDATA、bili_jct 等凭据，认证包里的 uid 和 buvid3 没变，已建立的连接可以继续用
					if (!await IsSameLoginAsync(userId, buvid3, cancellationToken))
					{
						LogIdentityChanged();
						await CloseForNewIdentityAsync(socket, activity, cancellationToken);
						return;
					}

					identityHash = current;
				}

				await SendAsync(socket, DanmuOperation.Heartbeat, ReadOnlyMemory<byte>.Empty, cancellationToken);
			}
			while (await timer.WaitForNextTickAsync(cancellationToken));
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			// 中止 socket 让接收失败，消费者拿到心跳的异常，不会一直卡着；换身份时关闭握手出错的连接仍按正常丢弃处理
			LogHeartbeatFailed(ex);
			activity.End ??= new ConnectionEnd.HeartbeatFailed(ex);
			socket.Abort();
		}
	}

	// 匿名连接在 Cookie 变化时总要重连，登录后才能换成实名连接
	private async ValueTask<bool> IsSameLoginAsync(long userId, string buvid3, CancellationToken cancellationToken)
	{
		BilibiliRequestOptions options = await identity.GetOptionsAsync(cancellationToken);
		return userId is not 0
			&& CookieHeader.GetValue(options.Cookie, UserIdCookieName) == userId.ToString(CultureInfo.InvariantCulture)
			&& identity.GetBuvid3(options) == buvid3;
	}

	// 直接中止会让挂起的接收失败，调用方会当成连接故障；走关闭握手则和服务器主动关闭一样正常结束
	private async Task CloseForNewIdentityAsync(ClientWebSocket socket, ReceiveActivity activity, CancellationToken cancellationToken)
	{
		activity.End = new ConnectionEnd.IdentityChanged();
		await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
		await Task.Delay(CloseTimeout, apiClient.TimeProvider, cancellationToken);

		// 服务器一直不回应关闭时，接收要等到接收超时才会结束
		if (socket.State is not WebSocketState.Closed)
		{
			socket.Abort();
		}
	}

	private static async ValueTask SendAsync(ClientWebSocket socket, DanmuOperation operation, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
	{
		byte[] packet = DanmuPacket.Encode(operation, SendProtocolVersion, body.Span);
		await socket.SendAsync(packet, WebSocketMessageType.Binary, true, cancellationToken);
	}

	private static async ValueTask<bool> ReceiveMessageAsync(ClientWebSocket socket, ArrayBufferWriter<byte> buffer, ReceiveActivity activity, CancellationToken cancellationToken)
	{
		try
		{
			while (true)
			{
				ValueWebSocketReceiveResult result = await socket.ReceiveAsync(buffer.GetMemory(ReceiveChunkSize), cancellationToken);

				if (result.MessageType is WebSocketMessageType.Close)
				{
					return false;
				}

				buffer.Advance(result.Count);

				if (buffer.WrittenCount > MaxMessageLength)
				{
					throw new InvalidDataException($"Danmu message exceeds {MaxMessageLength} bytes.");
				}

				if (result.EndOfMessage)
				{
					return true;
				}
			}
		}
		// 心跳中止 socket 时，挂起的接收以没人请求的取消失败，没有挂起的接收则下一次接收遇到已释放的 socket；
		// 换身份后服务器不回应关闭而是直接断开，也只是结束了这条本来就要丢弃的连接
		catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException && !cancellationToken.IsCancellationRequested && activity.End is { } end)
		{
			return end switch
			{
				ConnectionEnd.IdentityChanged => false,
				ConnectionEnd.TimedOut => throw new TimeoutException($"No danmu packet received within {ReceiveTimeout}.", ex),
				ConnectionEnd.HeartbeatFailed failed => throw new WebSocketException(WebSocketError.ConnectionClosedPrematurely, failed.Exception)
			};
		}
	}

	private async ValueTask CloseOutputAsync(ClientWebSocket socket)
	{
		if (socket.State is not WebSocketState.CloseReceived)
		{
			return;
		}

		try
		{
			using CancellationTokenSource cts = new(CloseTimeout, apiClient.TimeProvider);
			await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
		}
		catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
		{
		}
	}

	[LoggerMessage(Level = LogLevel.Warning, Message = "No danmu packet received for {Idle}, reconnecting")]
	private partial void LogReceiveTimedOut(TimeSpan idle);

	[LoggerMessage(Level = LogLevel.Information, Message = "Connecting to danmu server {Uri} for room {RoomId}")]
	private partial void LogConnecting(long roomId, Uri uri);

	[LoggerMessage(Level = LogLevel.Information, Message = "Entered room {RoomId}")]
	private partial void LogAuthenticated(long roomId);

	[LoggerMessage(Level = LogLevel.Information, Message = "Danmu server closed the connection of room {RoomId}: {Status} {Description}")]
	private partial void LogServerClosed(long roomId, WebSocketCloseStatus? status, string? description);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to get the current user id, connecting anonymously")]
	private partial void LogUserIdFailed(Exception exception);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Danmu server info of room {RoomId} has no token or host, using the default server")]
	private partial void LogNoServer(long roomId);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Danmu server info of room {RoomId} was blocked by risk control, retrying anonymously")]
	private partial void LogRiskControl(long roomId);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to get danmu server info of room {RoomId}, using the default server")]
	private partial void LogServerInfoFailed(Exception exception, long roomId);

	[LoggerMessage(Level = LogLevel.Debug, Message = "Skipped a malformed danmu message")]
	private partial void LogMalformedMessage(Exception exception);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Danmu heartbeat failed")]
	private partial void LogHeartbeatFailed(Exception exception);

	[LoggerMessage(Level = LogLevel.Information, Message = "Login state changed, reconnecting to the danmu server")]
	private partial void LogIdentityChanged();

	private readonly record struct DanmuEndpoint(string Host, ushort Port, string? Token, long UserId, string IdentityHash);

	private sealed record DanmuServer(DanmuServerInfo Info, long UserId, long FetchedAt, string IdentityHash);

	private sealed class ReceiveActivity(TimeProvider timeProvider)
	{
		private long _lastReceived = timeProvider.GetTimestamp();

		public TimeSpan Idle => timeProvider.GetElapsedTime(Volatile.Read(ref _lastReceived));

		// 心跳在中止 socket 之前写入，接收一方据此把失败的接收换成结束原因
		public ConnectionEnd? End
		{
			get => Volatile.Read(ref field);
			set => Volatile.Write(ref field, value);
		}

		public void Touch()
		{
			Volatile.Write(ref _lastReceived, timeProvider.GetTimestamp());
		}
	}

	private closed record ConnectionEnd
	{
		public sealed record TimedOut : ConnectionEnd;

		public sealed record IdentityChanged : ConnectionEnd;

		public sealed record HeartbeatFailed(Exception Exception) : ConnectionEnd;
	}
}
