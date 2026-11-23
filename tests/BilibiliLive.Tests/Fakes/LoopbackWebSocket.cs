using CryptoBase.Hashes;
using CryptoBase.Hashes.Sha1;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

namespace BilibiliLive.Tests.Fakes;

internal static class LoopbackWebSocket
{
	private static ReadOnlySpan<byte> AcceptGuid => "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"u8;

	public static async Task<(HttpResponseMessage Response, WebSocket Server)> AcceptAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		string key = request.Headers.GetValues("Sec-WebSocket-Key").Single();
		Span<byte> input = stackalloc byte[key.Length + AcceptGuid.Length];
		int length = Encoding.ASCII.GetBytes(key, input);
		AcceptGuid.CopyTo(input.Slice(length));
		Span<byte> hash = stackalloc byte[HashAlgorithm<Sha1Core>.HashLength];
		HashAlgorithm<Sha1Core>.HashData(input, hash);
		string accept = Convert.ToBase64String(hash);

		using Socket listener = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
		listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
		listener.Listen(1);

		Socket client = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
		Socket server;

		try
		{
			await client.ConnectAsync(listener.LocalEndPoint ?? throw new InvalidOperationException("The listener is not bound."), cancellationToken);
			server = await listener.AcceptAsync(cancellationToken);
		}
		catch
		{
			client.Dispose();
			throw;
		}

		HttpResponseMessage response = new(HttpStatusCode.SwitchingProtocols) { Content = new DuplexContent(new NetworkStream(client, true)) };
		response.Headers.Connection.Add("Upgrade");
		response.Headers.Upgrade.Add(new ProductHeaderValue("websocket"));
		response.Headers.TryAddWithoutValidation("Sec-WebSocket-Accept", accept);

		WebSocket serverSocket = WebSocket.CreateFromStream(new NetworkStream(server, true), new WebSocketCreationOptions { IsServer = true, KeepAliveInterval = Timeout.InfiniteTimeSpan });

		return (response, serverSocket);
	}

	// StreamContent 会把流包装成只读，但升级后的连接必须可写
	private sealed class DuplexContent(Stream connection) : HttpContent
	{
		protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
		{
			return connection.CopyToAsync(stream);
		}

		protected override bool TryComputeLength(out long length)
		{
			length = 0;
			return false;
		}

		protected override Stream CreateContentReadStream(CancellationToken cancellationToken)
		{
			return connection;
		}

		protected override Task<Stream> CreateContentReadStreamAsync()
		{
			return Task.FromResult(connection);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				connection.Dispose();
			}

			base.Dispose(disposing);
		}
	}
}
