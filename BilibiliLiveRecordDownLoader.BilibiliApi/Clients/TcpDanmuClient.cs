using BilibiliApi.Model.DanmuConf;
using Microsoft;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Pipelines.Extensions;
using Pipelines.Extensions.SocketPipe;
using System.IO.Pipelines;
using System.Net.Sockets;

namespace BilibiliApi.Clients;

public class TcpDanmuClient(ILogger<TcpDanmuClient> logger, BilibiliApiClient apiClient, IDistributedCache cacheService)
	: DanmuClientBase(logger, apiClient, cacheService)
{
	protected override string Server => $@"{Host}:{Port}";

	protected override ushort DefaultPort => 2243;

	private TcpClient? _client;

	private static readonly SocketPipeReaderOptions ReaderOptions = new(shutDownReceive: false);
	private static readonly SocketPipeWriterOptions WriterOptions = new(shutDownSend: false);

	protected override ushort GetPort(HostServerList server)
	{
		return server.port;
	}

	protected override IDisposable CreateClient()
	{
		return _client = new TcpClient();
	}

	protected override async ValueTask<IDuplexPipe> ClientHandshakeAsync(CancellationToken cancellationToken)
	{
		Assumes.NotNull(_client);
		Assumes.NotNullOrEmpty(Host);

		await _client.ConnectAsync(Host, Port, cancellationToken);
		return _client.Client.AsDuplexPipe(ReaderOptions, WriterOptions);
	}
}
