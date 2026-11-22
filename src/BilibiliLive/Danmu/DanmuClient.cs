using BilibiliLive.Api;
using BilibiliLive.Http;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BilibiliLive.Danmu;

public sealed partial class DanmuClient(BilibiliApiClient apiClient, BilibiliIdentity identity, ILogger<DanmuClient>? logger = null)
{
	internal const string DefaultHost = "broadcastlv.chat.bilibili.com";
	internal const ushort DefaultPort = 443;
	internal const int MaxMessageLength = 8 * 1024 * 1024;

	private const ushort SendProtocolVersion = 1;
	private const ushort ReceiveProtocolVersion = 3;
	private const int ReceiveChunkSize = 16 * 1024;

	internal static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

	// The server answers every heartbeat, so silence this long means a half-open connection
	internal static readonly TimeSpan ReceiveTimeout = HeartbeatInterval * 2.5;
	private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(5);

	// Rooms reconnect within seconds, refetching on every attempt would trip risk control
	internal static readonly TimeSpan ServerCacheDuration = TimeSpan.FromMinutes(1);

	// Never handed out, so it stays at the defaults
	private static readonly DanmuReceiveOptions DefaultOptions = new();

	private readonly ILogger _logger = logger ?? NullLogger<DanmuClient>.Instance;
	private readonly ConcurrentDictionary<long, DanmuServer> _servers = new();

	public IAsyncEnumerable<DanmuMessage> ReceiveAsync(long roomId, CancellationToken cancellationToken = default)
	{
		return ReceiveAsync(roomId, DefaultOptions, cancellationToken);
	}

	public async IAsyncEnumerable<DanmuMessage> ReceiveAsync(long roomId, DanmuReceiveOptions options, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		await using IAsyncEnumerator<DanmuMessage> enumerator = ReceiveCoreAsync(roomId, options, cancellationToken).GetAsyncEnumerator(cancellationToken);

		while (true)
		{
			bool hasNext;

			try
			{
				hasNext = await enumerator.MoveNextAsync();
			}
			catch (Exception) when (cancellationToken.IsCancellationRequested)
			{
				// Cancelling a pending WebSocket operation aborts the socket, which surfaces as various exception types
				hasNext = false;
			}

			if (!hasNext)
			{
				yield break;
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

		// A uid 0 token comes with the anonymous cookie, the handshake has to present the same identity as the auth packet
		if (endpoint.UserId is 0)
		{
			socket.Options.SetRequestHeader(BilibiliRequestHandler.AnonymousHeader, "1");
		}

		// Going through the api HttpClient keeps the proxy, User-Agent and buvid3 of the WebSocket handshake identical to the API requests
		await socket.ConnectAsync(uri, apiClient.HttpClient, cancellationToken);

		DanmuAuthBody auth = new(endpoint.UserId, roomId, ReceiveProtocolVersion, "web", 2, await identity.GetBuvid3Async(cancellationToken), endpoint.Token);
		await SendAsync(socket, DanmuOperation.Auth, JsonSerializer.SerializeToUtf8Bytes(auth, BilibiliJsonContext.Default.DanmuAuthBody), cancellationToken);

		using CancellationTokenSource heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		ReceiveActivity activity = new(apiClient.TimeProvider);
		Task heartbeat = HeartbeatAsync(socket, activity, endpoint.IdentityHash, heartbeatCts.Token);
		ArrayBufferWriter<byte> buffer = new(ReceiveChunkSize);
		List<DanmuMessage> messages = [];

		try
		{
			while (await ReceiveMessageAsync(socket, buffer, activity, cancellationToken))
			{
				activity.Touch();

				// Taken once the whole frame is in, so batching on the server does not spread one frame over several arrival times
				DanmuReceiveTime receivedAt = new(apiClient.TimeProvider.GetTimestamp(), apiClient.TimeProvider.GetUtcNow());
				DanmuParseMode mode = options.Mode;

				foreach (DanmuPacket packet in DanmuPacket.Decode(buffer.WrittenMemory))
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
							TryParse(packet.Body, mode, receivedAt, messages);

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

			if (!activity.IsIdentityChanged)
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
		// Taken before the requests, so a login during them only causes one more reconnect instead of a stale token passing for the new identity
		string identityHash = await GetIdentityHashAsync(cancellationToken);

		// A token only authenticates the uid it was issued for
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

		// Picking a random host lets the caller's reconnects move away from a broken one
		DanmuHost host = server.Info.Hosts[Random.Shared.Next(server.Info.Hosts.Count)];
		return new DanmuEndpoint(host.Host, host.WssPort, server.Info.Token, server.UserId, identityHash);
	}

	// Only a hash is kept, so the cookie cannot leak through anything that logs the cached server
	private async ValueTask<string> GetIdentityHashAsync(CancellationToken cancellationToken)
	{
		(_, string cookie) = await identity.ResolveAsync(false, cancellationToken);
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cookie)));
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
			// A token requested with the login cookie would not match uid 0
			LogUserIdFailed(ex);
			return await FetchAnonymousServerAsync(roomId, identityHash, cancellationToken);
		}

		try
		{
			return ToServer(roomId, await apiClient.GetDanmuServerInfoAsync(roomId, cancellationToken), userId, identityHash);
		}
		// Only risk control of the account is worth an anonymous retry; -412 and HTTP 412 block the IP, which an anonymous request shares
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
			return ToServer(roomId, await apiClient.GetAnonymousDanmuServerInfoAsync(roomId, cancellationToken), 0, identityHash);
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

	private void TryParse(ReadOnlyMemory<byte> body, DanmuParseMode mode, DanmuReceiveTime receivedAt, List<DanmuMessage> output)
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

	private async Task HeartbeatAsync(ClientWebSocket socket, ReceiveActivity activity, string identityHash, CancellationToken cancellationToken)
	{
		try
		{
			using PeriodicTimer timer = new(HeartbeatInterval, apiClient.TimeProvider);

			do
			{
				// Sends into a dead TCP connection still succeed, only the missing replies reveal it
				TimeSpan idle = activity.Idle;

				if (idle > ReceiveTimeout)
				{
					LogReceiveTimedOut(idle);
					activity.MarkTimedOut();
					socket.Abort();
					return;
				}

				// The identity of a connection is fixed by its auth packet, so logging in or out only takes effect on a new connection
				if (await GetIdentityHashAsync(cancellationToken) != identityHash)
				{
					LogIdentityChanged();
					await CloseForNewIdentityAsync(socket, activity, cancellationToken);
					return;
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
			// Aborting fails the pending receive, so the failure reaches the consumer instead of a silent stall
			LogHeartbeatFailed(ex);
			socket.Abort();
		}
	}

	// Aborting would fail the pending receive, which the caller takes for a broken connection; a close handshake ends it like the server closing
	private async Task CloseForNewIdentityAsync(ClientWebSocket socket, ReceiveActivity activity, CancellationToken cancellationToken)
	{
		activity.MarkIdentityChanged();
		await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
		await Task.Delay(CloseTimeout, apiClient.TimeProvider, cancellationToken);

		// A server that never answers the close would leave the receive waiting until the receive timeout
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
		// The connection is discarded after an identity change, so a server dropping it instead of answering the close is no failure either
		catch (Exception ex) when (ex is OperationCanceledException or WebSocketException && !cancellationToken.IsCancellationRequested && activity.IsIdentityChanged)
		{
			return false;
		}
		catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
		{
			// The heartbeat aborted the socket, which fails the pending receive as a cancellation nobody requested
			throw activity.IsTimedOut ? new TimeoutException($"No danmu packet received within {ReceiveTimeout}.", ex) : new WebSocketException(WebSocketError.ConnectionClosedPrematurely, ex);
		}
	}

	private static async ValueTask CloseOutputAsync(ClientWebSocket socket)
	{
		if (socket.State is not WebSocketState.CloseReceived)
		{
			return;
		}

		try
		{
			using CancellationTokenSource cts = new(CloseTimeout);
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

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to send danmu heartbeat")]
	private partial void LogHeartbeatFailed(Exception exception);

	[LoggerMessage(Level = LogLevel.Information, Message = "Login state changed, reconnecting to the danmu server")]
	private partial void LogIdentityChanged();

	private readonly record struct DanmuEndpoint(string Host, ushort Port, string? Token, long UserId, string IdentityHash);

	private sealed record DanmuServer(DanmuServerInfo Info, long UserId, long FetchedAt, string IdentityHash);

	private sealed class ReceiveActivity(TimeProvider timeProvider)
	{
		private long _lastReceived = timeProvider.GetTimestamp();
		private volatile bool _isTimedOut;
		private volatile bool _isIdentityChanged;

		public TimeSpan Idle => timeProvider.GetElapsedTime(Volatile.Read(ref _lastReceived));

		public bool IsTimedOut => _isTimedOut;

		public bool IsIdentityChanged => _isIdentityChanged;

		public void Touch()
		{
			Volatile.Write(ref _lastReceived, timeProvider.GetTimestamp());
		}

		public void MarkTimedOut()
		{
			_isTimedOut = true;
		}

		public void MarkIdentityChanged()
		{
			_isIdentityChanged = true;
		}
	}
}
