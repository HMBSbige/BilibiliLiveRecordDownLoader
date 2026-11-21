using BilibiliLive.Api;
using BilibiliLive.Http;
using System.Buffers;
using System.Collections.Concurrent;
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
	private const ushort ReceiveProtocolVersion = 3;
	private const long RiskControlCode = -352;
	private const int ReceiveChunkSize = 16 * 1024;

	internal static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

	// The server answers every heartbeat, so silence this long means a half-open connection
	internal static readonly TimeSpan ReceiveTimeout = HeartbeatInterval * 2.5;
	private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(5);

	// Rooms reconnect within seconds, refetching on every attempt would trip risk control
	internal static readonly TimeSpan ServerCacheDuration = TimeSpan.FromMinutes(1);

	private readonly ILogger _logger = logger ?? NullLogger<DanmuClient>.Instance;
	private readonly ConcurrentDictionary<long, DanmuServer> _servers = new();

	public async IAsyncEnumerable<DanmuMessage> ReceiveAsync(long roomId, [EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		await using IAsyncEnumerator<DanmuMessage> enumerator = ReceiveCoreAsync(roomId, cancellationToken).GetAsyncEnumerator(cancellationToken);

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

	private async IAsyncEnumerable<DanmuMessage> ReceiveCoreAsync(long roomId, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		DanmuEndpoint endpoint = await ResolveEndpointAsync(roomId, cancellationToken);
		Uri uri = new($"wss://{endpoint.Host}:{endpoint.Port}/sub");

		LogConnecting(roomId, uri);

		using ClientWebSocket socket = new();
		// Going through the api HttpClient keeps the proxy, User-Agent and Cookie of the WebSocket handshake identical to the API requests
		await socket.ConnectAsync(uri, apiClient.HttpClient, cancellationToken);

		DanmuAuthBody auth = new(endpoint.UserId, roomId, ReceiveProtocolVersion, "web", 2, await identity.GetBuvid3Async(cancellationToken), endpoint.Token);
		await SendAsync(socket, DanmuOperation.Auth, JsonSerializer.SerializeToUtf8Bytes(auth, BilibiliJsonContext.Default.DanmuAuthBody), cancellationToken);

		using CancellationTokenSource heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		ReceiveActivity activity = new(apiClient.TimeProvider);
		Task heartbeat = HeartbeatAsync(socket, activity, heartbeatCts.Token);
		ArrayBufferWriter<byte> buffer = new(ReceiveChunkSize);

		try
		{
			while (await ReceiveMessageAsync(socket, buffer, activity, cancellationToken))
			{
				activity.Touch();

				foreach (DanmuPacket packet in DanmuPacket.Decode(buffer.WrittenMemory))
				{
					switch (packet.Operation)
					{
						case DanmuOperation.AuthReply:
						{
							EnsureAuthenticated(roomId, packet.Body);
							break;
						}
						case DanmuOperation.Message when TryParse(packet.Body) is { } message:
						{
							yield return message;
							break;
						}
					}
				}

				buffer.ResetWrittenCount();
			}

			LogServerClosed(roomId, socket.CloseStatus, socket.CloseStatusDescription);
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
		if (!_servers.TryGetValue(roomId, out DanmuServer? server) || apiClient.TimeProvider.GetElapsedTime(server.FetchedAt) >= ServerCacheDuration)
		{
			server = await FetchServerAsync(roomId, cancellationToken);

			if (server is null)
			{
				_servers.TryRemove(roomId, out _);

				// The token is issued for the requesting uid, so without a token the connection has to be anonymous
				return new DanmuEndpoint(DefaultHost, DefaultPort, null, 0);
			}

			_servers[roomId] = server;
		}

		// Picking a random host lets the caller's reconnects move away from a broken one
		DanmuHost host = server.Info.Hosts[Random.Shared.Next(server.Info.Hosts.Count)];
		return new DanmuEndpoint(host.Host, host.WssPort, server.Info.Token, server.UserId);
	}

	private async Task<DanmuServer?> FetchServerAsync(long roomId, CancellationToken cancellationToken)
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
			return await FetchAnonymousServerAsync(roomId, cancellationToken);
		}

		try
		{
			return ToServer(roomId, await apiClient.GetDanmuServerInfoAsync(roomId, cancellationToken), userId);
		}
		catch (BilibiliApiException ex) when (ex.Code is RiskControlCode)
		{
			LogRiskControl(roomId);
			return await FetchAnonymousServerAsync(roomId, cancellationToken);
		}
		catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
		{
			LogServerInfoFailed(ex, roomId);
			return null;
		}
	}

	private async Task<DanmuServer?> FetchAnonymousServerAsync(long roomId, CancellationToken cancellationToken)
	{
		try
		{
			return ToServer(roomId, await apiClient.GetAnonymousDanmuServerInfoAsync(roomId, cancellationToken), 0);
		}
		catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
		{
			LogServerInfoFailed(ex, roomId);
			return null;
		}
	}

	private DanmuServer? ToServer(long roomId, DanmuServerInfo info, long userId)
	{
		if (info is { Token.Length: > 0, Hosts.Count: > 0 })
		{
			return new DanmuServer(info, userId, apiClient.TimeProvider.GetTimestamp());
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

	private DanmuMessage? TryParse(ReadOnlyMemory<byte> body)
	{
		try
		{
			return DanmuMessageParser.Parse(body);
		}
		catch (JsonException ex)
		{
			LogMalformedMessage(ex);
			return null;
		}
	}

	private async Task HeartbeatAsync(ClientWebSocket socket, ReceiveActivity activity, CancellationToken cancellationToken)
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

	private readonly record struct DanmuEndpoint(string Host, ushort Port, string? Token, long UserId);

	private sealed record DanmuServer(DanmuServerInfo Info, long UserId, long FetchedAt);

	private sealed class ReceiveActivity(TimeProvider timeProvider)
	{
		private long _lastReceived = timeProvider.GetTimestamp();
		private volatile bool _isTimedOut;

		public TimeSpan Idle => timeProvider.GetElapsedTime(Volatile.Read(ref _lastReceived));

		public bool IsTimedOut => _isTimedOut;

		public void Touch()
		{
			Volatile.Write(ref _lastReceived, timeProvider.GetTimestamp());
		}

		public void MarkTimedOut()
		{
			_isTimedOut = true;
		}
	}
}
