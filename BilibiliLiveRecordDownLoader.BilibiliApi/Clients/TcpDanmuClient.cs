using BilibiliApi.Model.DanmuConf;
using Microsoft.Extensions.Logging;
using Pipelines.Extensions;
using Pipelines.Extensions.SocketPipe;
using System;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliApi.Clients
{
	public class TcpDanmuClient : DanmuClientBase
	{
		protected override string Server => $@"{Host}:{Port}";
		protected override ushort DefaultPort => 2243;
		protected override bool ClientConnected => _client?.Connected ?? false;

		private TcpClient? _client;

		private static readonly SocketPipeReaderOptions ReaderOptions = new(shutDownReceive: false);
		private static readonly SocketPipeWriterOptions WriterOptions = new(shutDownSend: false);

		public TcpDanmuClient(ILogger<TcpDanmuClient> logger, BilibiliApiClient apiClient) : base(logger, apiClient) { }

		protected override ushort GetPort(HostServerList server)
		{
			return server.port;
		}

		protected override IDisposable CreateClient()
		{
			return _client = new TcpClient();
		}

		protected override async ValueTask<IDuplexPipe> ClientHandshakeAsync(CancellationToken token)
		{
			await _client!.ConnectAsync(Host!, Port, token);
			return _client.Client.AsDuplexPipe(ReaderOptions, WriterOptions);
		}
	}
}
