using BilibiliApi.Model.DanmuConf;
using Microsoft.Extensions.Logging;
using System;
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
		private NetworkStream? _netStream;

		public TcpDanmuClient(ILogger logger) : base(logger) { }

		protected override ushort GetPort(HostServerList server)
		{
			return server.port;
		}

		protected override IDisposable CreateClient()
		{
			_client = new();
			return _client;
		}

		protected override async ValueTask ClientHandshakeAsync(CancellationToken token)
		{
			await _client!.ConnectAsync(Host!, Port, token);
			_netStream = _client.GetStream();
		}

		protected override async ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken token)
		{
			await _netStream!.WriteAsync(buffer, token);
			await _netStream.FlushAsync(token);
		}

		protected override async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken token)
		{
			return await _netStream!.ReadAsync(buffer, token);
		}
	}
}
