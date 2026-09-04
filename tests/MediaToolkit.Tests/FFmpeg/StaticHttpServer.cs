using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MediaToolkit.Tests.FFmpeg;

// 用 TcpListener 而不是 HttpListener：后者在 Windows 上监听非 localhost 前缀需要管理员权限
internal sealed class StaticHttpServer : IAsyncDisposable
{
	private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
	private readonly CancellationTokenSource _cts = new();
	private readonly Task _acceptLoop;

	public StaticHttpServer(byte[] body)
	{
		_listener.Start();
		_acceptLoop = AcceptLoopAsync(body, _cts.Token);
	}

	public ConcurrentQueue<string> RequestHeads { get; } = [];

	public Uri GetUri(string pathAndQuery)
	{
		return new Uri($@"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}{pathAndQuery}");
	}

	private async Task AcceptLoopAsync(byte[] body, CancellationToken cancellationToken)
	{
		byte[] responseHead = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: video/x-flv\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");

		while (!cancellationToken.IsCancellationRequested)
		{
			using TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken);
			await using NetworkStream stream = client.GetStream();
			using StreamReader reader = new(stream, Encoding.ASCII, false, 1024, true);
			StringBuilder head = new();

			while (await reader.ReadLineAsync(cancellationToken) is { Length: > 0 } line)
			{
				// AppendLine 在 Linux 上只追加 \n，断言要按 HTTP 的 \r\n 匹配
				head.Append(line).Append("\r\n");
			}

			RequestHeads.Enqueue(head.ToString());

			try
			{
				await stream.WriteAsync(responseHead, cancellationToken);
				await stream.WriteAsync(body, cancellationToken);
			}
			catch (IOException)
			{
				// 客户端读够了就会提前断开
			}
		}
	}

	public async ValueTask DisposeAsync()
	{
		await _cts.CancelAsync();
		_listener.Stop();
		await _acceptLoop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
		_cts.Dispose();
	}
}
