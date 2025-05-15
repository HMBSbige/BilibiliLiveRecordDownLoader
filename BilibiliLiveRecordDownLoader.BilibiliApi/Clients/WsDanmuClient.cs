using BilibiliApi.Model.DanmuConf;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace BilibiliApi.Clients;

public class WsDanmuClient(ILogger<WsDanmuClient> logger, BilibiliApiClient apiClient, IDistributedCache cacheService)
	: WssDanmuClient(logger, apiClient, cacheService)
{
	protected override string Server => $@"ws://{Host}:{Port}/sub";

	protected override ushort DefaultPort => 2244;

	protected override ushort GetPort(HostServerList server)
	{
		return server.ws_port;
	}
}
