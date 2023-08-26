using BilibiliApi.Model.DanmuConf;
using Microsoft;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Pipelines.Extensions;
using System.IO.Pipelines;
using System.Net;
using System.Net.WebSockets;

namespace BilibiliApi.Clients;

public class WssDanmuClient : DanmuClientBase
{
	protected override string Server => $@"wss://{Host}:{Port}/sub";
	protected override ushort DefaultPort => 443;

	private ClientWebSocket? _client;

	public WssDanmuClient(ILogger<WssDanmuClient> logger, BilibiliApiClient apiClient, IDistributedCache cacheService) : base(logger, apiClient, cacheService) { }

	protected override ushort GetPort(HostServerList server)
	{
		return server.wss_port;
	}

	protected override IDisposable CreateClient()
	{
		_client = new ClientWebSocket();
		_client.Options.Proxy = WebRequest.DefaultWebProxy;
		_client.Options.SetRequestHeader(@"User-Agent", ApiClient.Client.DefaultRequestHeaders.UserAgent.ToString());
		return _client;
	}

	protected override async ValueTask<IDuplexPipe> ClientHandshakeAsync(CancellationToken cancellationToken)
	{
		Assumes.NotNull(_client);

		await _client.ConnectAsync(new Uri(Server), cancellationToken);
		return _client.AsDuplexPipe();
	}
}
