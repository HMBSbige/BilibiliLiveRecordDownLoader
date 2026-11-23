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
		using Socket listener = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
		listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
		listener.Listen(1);

		Socket client = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
		ValueTask connect = client.ConnectAsync(listener.LocalEndPoint ?? throw new InvalidOperationException("The listener is not bound."), cancellationToken);
		Socket server = await listener.AcceptAsync(cancellationToken);
		await connect;

		string key = request.Headers.GetValues("Sec-WebSocket-Key").Single();
		Span<byte> input = stackalloc byte[key.Length + AcceptGuid.Length];
		int length = Encoding.ASCII.GetBytes(key, input);
		AcceptGuid.CopyTo(input.Slice(length));
		Span<byte> hash = stackalloc byte[HashAlgorithm<Sha1Core>.HashLength];
		HashAlgorithm<Sha1Core>.HashData(input, hash);
		string accept = Convert.ToBase64String(hash);

		HttpResponseMessage response = new(HttpStatusCode.SwitchingProtocols) { Content = new DuplexContent(new NetworkStream(client, true)) };
		response.Headers.Connection.Add("Upgrade");
		response.Headers.Upgrade.Add(new ProductHeaderValue("websocket"));
		response.Headers.TryAddWithoutValidation("Sec-WebSocket-Accept", accept);

		WebSocket serverSocket = WebSocket.CreateFromStream(new NetworkStream(server, true), new WebSocketCreationOptions { IsServer = true, KeepAliveInterval = Timeout.InfiniteTimeSpan });

		return (response, serverSocket);
	}

	// StreamContent wraps its stream read-only, but the upgraded connection has to be writable
	private sealed class DuplexContent(Stream stream) : HttpContent
	{
		protected override Task SerializeToStreamAsync(Stream target, TransportContext? context)
		{
			return stream.CopyToAsync(target);
		}

		protected override bool TryComputeLength(out long length)
		{
			length = 0;
			return false;
		}

		protected override Stream CreateContentReadStream(CancellationToken cancellationToken)
		{
			return stream;
		}

		protected override Task<Stream> CreateContentReadStreamAsync()
		{
			return Task.FromResult(stream);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				stream.Dispose();
			}

			base.Dispose(disposing);
		}
	}
}
