using BilibiliLiveRecordDownLoader.Application.Http;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

// 用真实的 SocketsHttpHandler 请求本机回环服务器，所有主机名都连到这台服务器
public sealed class BilibiliHttpClientsTest
{
	private const string LoginCookie = "SESSDATA=secret";
	private const string RedirectTarget = "http://cdn.test/live.flv";

	private static ServiceProvider CreateServices(LoopbackServer server, string clientName)
	{
		AppConfig config = AppConfig.CreateDefault();
		config.Cookie = LoginCookie;

		ServiceCollection services = new();
		services.AddSingleton<IAppConfigManager>(new FakeConfigManager(config));
		services.AddBilibiliHttpClients();
		services.AddHttpClient(clientName).ConfigurePrimaryHttpMessageHandler((handler, _) => ((SocketsHttpHandler)handler).ConnectCallback = server.ConnectAsync);
		return services.BuildServiceProvider();
	}

	[Test]
	[Timeout(10_000)]
	public async Task ApiClientDoesNotFollowRedirectsAsync(CancellationToken cancellationToken)
	{
		await using LoopbackServer server = new();
		await using ServiceProvider services = CreateServices(server, HttpClientNames.BilibiliApi);
		HttpClient client = services.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientNames.BilibiliApi);

		using HttpResponseMessage response = await client.GetAsync("http://api.live.bilibili.com/redirect", cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Found);
			await Assert.That(string.Join(", ", server.Requests.Select(static request => request.Host))).IsEqualTo("api.live.bilibili.com");
			await Assert.That(server.Requests.Single().Cookie).Contains(LoginCookie);
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task StreamClientFollowsRedirectsWithoutTheLoginCookieAsync(CancellationToken cancellationToken)
	{
		await using LoopbackServer server = new();
		await using ServiceProvider services = CreateServices(server, HttpClientNames.BilibiliStream);
		HttpClient client = services.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientNames.BilibiliStream);

		using HttpResponseMessage response = await client.GetAsync("http://live.bilibili.com/redirect", cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
			await Assert.That(string.Join(", ", server.Requests.Select(static request => $"{request.Host} Cookie={request.Cookie}"))).IsEqualTo("live.bilibili.com Cookie=, cdn.test Cookie=");
		}
	}

	private sealed record ReceivedRequest(string Host, string? Cookie);

	// 收到 /redirect 时重定向到另一个主机，其余请求返回空的 200
	private sealed class LoopbackServer : IAsyncDisposable
	{
		private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
		private readonly CancellationTokenSource _stop = new();
		private readonly Task _serving;

		public LoopbackServer()
		{
			_listener.Start();
			_serving = ServeAsync(_stop.Token);
		}

		public ConcurrentQueue<ReceivedRequest> Requests { get; } = new();

		public async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
		{
			Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

			try
			{
				await socket.ConnectAsync(_listener.LocalEndpoint, cancellationToken);
				return new NetworkStream(socket, true);
			}
			catch
			{
				socket.Dispose();
				throw;
			}
		}

		public async ValueTask DisposeAsync()
		{
			await _stop.CancelAsync();
			_listener.Stop();
			await _serving.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
			_stop.Dispose();
		}

		private async Task ServeAsync(CancellationToken cancellationToken)
		{
			while (true)
			{
				using TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken);
				await using NetworkStream stream = client.GetStream();
				using StreamReader reader = new(stream, Encoding.ASCII, false, 1024, true);

				string path = (await reader.ReadLineAsync(cancellationToken))?.Split(' ')[1] ?? string.Empty;
				Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);

				while (await reader.ReadLineAsync(cancellationToken) is { Length: > 0 } line)
				{
					string[] header = line.Split(':', 2, StringSplitOptions.TrimEntries);
					headers[header[0]] = header[1];
				}

				Requests.Enqueue(new ReceivedRequest(headers["Host"], headers.GetValueOrDefault("Cookie")));

				string status = path is "/redirect" ? $"302 Found\r\nLocation: {RedirectTarget}" : "200 OK";
				await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), cancellationToken);
			}
		}
	}
}
