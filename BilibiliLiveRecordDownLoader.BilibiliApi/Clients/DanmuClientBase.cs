using BilibiliApi.Enums;
using BilibiliApi.Model.Danmu;
using BilibiliApi.Model.DanmuConf;
using BilibiliLiveRecordDownLoader.Shared.Utils;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.IO.Pipelines;
using System.Linq;
using System.Net.Http;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliApi.Clients
{
	public abstract class DanmuClientBase : IDanmuClient
	{
		private readonly ILogger _logger;

		public long RoomId { get; set; }

		public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(2);

		private readonly Subject<DanmuPacket> _danMuSubj = new();
		public IObservable<DanmuPacket> Received => _danMuSubj.AsObservable();

		public BililiveApiClient? ApiClient { get; set; }

		protected string? Host;
		protected ushort Port;
		protected abstract string Server { get; }
		private string? _token;
		private long _uid;
		private const long ProtocolVersion = 2;

		private const string DefaultHost = @"broadcastlv.chat.bilibili.com";
		protected abstract ushort DefaultPort { get; }

		protected abstract bool ClientConnected { get; }

		private IDisposable? _heartBeatTask;

		private CancellationTokenSource? _cts;
		protected readonly CompositeDisposable DisposableServices = new();

		private const int BufferSize = 1024;

		private string logHeader => $@"[{RoomId}]";

		protected DanmuClientBase(ILogger logger)
		{
			_logger = logger;
		}

		protected abstract ushort GetPort(HostServerList server);

		protected abstract ValueTask ClientHandshakeAsync(CancellationToken token);

		protected abstract ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken token);

		protected abstract ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken token);

		public virtual async ValueTask StartAsync()
		{
			if (_isDisposed)
			{
				throw new ObjectDisposedException(GetType().FullName);
			}

			await StopAsync();

			_cts = new();

			await ConnectWithRetryAsync(_cts.Token);
		}

		private async ValueTask GetServerAsync(CancellationToken token)
		{
			try
			{
				_uid = default;
				_token = default;
				Host = default;
				Port = default;

				if (ApiClient is null)
				{
					return;
				}

				var conf = await ApiClient.GetDanmuConfAsync(RoomId, token);
				if (conf?.data?.host_server_list is null || conf.data.host_server_list.Length == 0)
				{
					throw new HttpRequestException(@"Wrong response");
				}

				var server = conf.data.host_server_list.First();
				Host = server.host;
				Port = GetPort(server);
				_token = conf.data.token;

				try
				{
					_uid = await ApiClient.GetUidAsync(token);
				}
				catch
				{
					// ignored
				}
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, @"{0} 获取弹幕服务器失败", logHeader);
			}
			finally
			{
				if (string.IsNullOrEmpty(_token) || string.IsNullOrWhiteSpace(Host))
				{
					_logger.LogWarning(@"{0} 使用默认弹幕服务器", logHeader);
					Host = DefaultHost;
				}

				if (Port == default)
				{
					Port = DefaultPort;
				}
			}
		}

		private async ValueTask WaitAsync(CancellationToken token)
		{
			try
			{
				await Task.Delay(RetryInterval, token);
			}
			catch (TaskCanceledException)
			{
				_logger.LogInformation(@"{0} 不再连接弹幕服务器", logHeader);
			}
		}

		private async ValueTask ConnectWithRetryAsync(CancellationToken token)
		{
			while (!ClientConnected && !token.IsCancellationRequested)
			{
				await GetServerAsync(token);

				_logger.LogInformation(@"{0} 正在连接弹幕服务器 {1}", logHeader, Server);

				if (!await ConnectAsync(token))
				{
					await WaitAsync(token);
					continue;
				}

				_logger.LogInformation(@"{0} 连接弹幕服务器成功", logHeader);

				ProcessDanMuAsync(token).NoWarning();

				break;
			}
		}

		private async ValueTask<bool> ConnectAsync(CancellationToken token)
		{
			try
			{
				await ClientHandshakeAsync(token);

				await SendAuthAsync(token);

				_heartBeatTask = Observable.Interval(TimeSpan.FromSeconds(30))
					.Subscribe(_ => SendHeartbeatAsync(token).NoWarning());
				DisposableServices.Add(_heartBeatTask);

				return true;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, @"{0} 连接弹幕服务器错误", logHeader);
				return false;
			}
		}

		private async ValueTask SendDataAsync(Operation operation, string body, CancellationToken token)
		{
			var data = Encoding.UTF8.GetBytes(body);
			var packet = new DanmuPacket
			{
				PacketLength = data.Length + 16,
				HeaderLength = 16,
				ProtocolVersion = 1,
				Operation = operation,
				SequenceId = 1,
				Body = data
			};

			using var memory = MemoryPool<byte>.Shared.Rent(packet.PacketLength);

			var buffer = packet.ToMemory(memory.Memory);

			await SendAsync(buffer, token);
		}

		private async ValueTask SendAuthAsync(CancellationToken token)
		{
			string json;
			if (string.IsNullOrEmpty(_token))
			{
				json = @$"{{""roomid"":{RoomId},""uid"":{_uid},""protover"":{ProtocolVersion}}}";
			}
			else
			{
				json = @$"{{""roomid"":{RoomId},""uid"":{_uid},""protover"":{ProtocolVersion},""key"":""{_token}""}}";
			}
			_logger.LogDebug(@"{0} AuthJson: {1}", logHeader, json);
			await SendDataAsync(Operation.Auth, json, token);
		}

		private async ValueTask SendHeartbeatAsync(CancellationToken token)
		{
			try
			{
				_logger.LogDebug(@"{0} 发送心跳包", logHeader);
				await SendDataAsync(Operation.Heartbeat, string.Empty, token);
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, @"{0} 心跳包发送失败", logHeader);
				ResetClient();
			}
		}

		private async ValueTask ProcessDanMuAsync(CancellationToken token)
		{
			try
			{
				var pipe = new Pipe();
				var writing = FillPipeAsync(pipe.Writer, token);
				var reading = ReadPipeAsync(pipe.Reader, token);
				await reading;
				await writing;
			}
			catch (OperationCanceledException)
			{
				_logger.LogInformation(@"{0} 不再连接弹幕服务器", logHeader);
			}
			catch (Exception ex)
			{
				ResetClient();

				_logger.LogWarning(ex, @"{0} 弹幕服务器连接被断开，尝试重连...", logHeader);
				await WaitAsync(token);
				await ConnectWithRetryAsync(token);
			}
		}

		private async ValueTask FillPipeAsync(PipeWriter writer, CancellationToken token)
		{
			try
			{
				while (!token.IsCancellationRequested)
				{
					var memory = writer.GetMemory(BufferSize);

					var bytesRead = await ReceiveAsync(memory, token);

					_logger.LogDebug(@"{0} 收到 {1} 字节", logHeader, bytesRead);

					if (bytesRead == 0)
					{
						break;
					}

					writer.Advance(bytesRead);

					var result = await writer.FlushAsync(token);

					if (result.IsCompleted)
					{
						break;
					}
				}
			}
			finally
			{
				await writer.CompleteAsync();
			}
		}

		private async ValueTask ReadPipeAsync(PipeReader reader, CancellationToken token)
		{
			try
			{
				while (!token.IsCancellationRequested)
				{
					var result = await reader.ReadAsync(token);
					var buffer = result.Buffer;
					try
					{
						while (buffer.Length >= 16)
						{
							var packet = new DanmuPacket();
							var success = packet.ReadDanMu(ref buffer);
							await ProcessDanMuPacketAsync(packet, token);

							if (!success)
							{
								break;
							}
						}

						if (result.IsCompleted)
						{
							break;
						}
					}
					finally
					{
						reader.AdvanceTo(buffer.Start, buffer.End);
					}
				}
			}
			finally
			{
				await reader.CompleteAsync();
			}
		}

		private async ValueTask ProcessDanMuPacketAsync(DanmuPacket packet, CancellationToken token)
		{
			switch (packet.ProtocolVersion)
			{
				case 0:
				case 1:
				{
					EmitDanmu(packet);
					break;
				}
				case 2:
				{
					await using var ms = new MemoryStream();
					await ms.WriteAsync(packet.Body[2..], token); // Drop header
					ms.Seek(0, SeekOrigin.Begin);

					await using var deflate = new DeflateStream(ms, CompressionMode.Decompress);

					using var header = MemoryPool<byte>.Shared.Rent(4);
					while (true)
					{
						var headerLength = await deflate.ReadAsync(header.Memory.Slice(0, 4), token);

						if (headerLength < 4)
						{
							break;
						}

						var packetLength = BinaryPrimitives.ReadInt32BigEndian(header.Memory.Span);
						var remainSize = packetLength - headerLength;

						using var subBuffer = MemoryPool<byte>.Shared.Rent(remainSize);

						await deflate.ReadAsync(subBuffer.Memory.Slice(0, remainSize), token);

						var subPacket = new DanmuPacket { PacketLength = packetLength };
						subPacket.ReadDanMu(subBuffer.Memory);

						await ProcessDanMuPacketAsync(subPacket, token);
					}

					break;
				}
				default:
				{
					_logger.LogWarning(@"{0} 弹幕协议不支持。Version: {1}", logHeader, packet.ProtocolVersion);
					break;
				}
			}
		}

		private void EmitDanmu(in DanmuPacket packet)
		{
#if DEBUG
			switch (packet.Operation)
			{
				case Operation.HeartbeatReply:
				{
					_logger.LogDebug(@"{0} 收到弹幕[{1}] 人气值: {2}", logHeader, packet.Operation, BinaryPrimitives.ReadUInt32BigEndian(packet.Body.Span));
					break;
				}
				case Operation.SendMsgReply:
				case Operation.AuthReply:
				{
					_logger.LogDebug(@"{0} 收到弹幕[{1}]:{2}", logHeader, packet.Operation, Encoding.UTF8.GetString(packet.Body.Span));
					break;
				}
				default:
				{
					_logger.LogDebug(@"{0} 收到弹幕[{1}]", logHeader, packet.Operation);
					break;
				}
			}
#endif
			_danMuSubj.OnNext(packet);
		}

		private void ResetClient()
		{
			DisposableServices.Clear();
		}

		public virtual ValueTask StopAsync()
		{
			_cts?.Cancel();
			ResetClient();
			return default;
		}

		private volatile bool _isDisposed;

		public virtual async ValueTask DisposeAsync()
		{
			if (_isDisposed)
			{
				return;
			}
			_isDisposed = true;

			_danMuSubj.OnCompleted();
			await StopAsync();
			_cts?.Dispose();
			DisposableServices.Dispose();
		}
	}
}
