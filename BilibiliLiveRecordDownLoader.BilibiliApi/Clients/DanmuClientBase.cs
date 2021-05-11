using BilibiliApi.Enums;
using BilibiliApi.Model.Danmu;
using BilibiliApi.Model.DanmuConf;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using System;
using System.Buffers;
using System.IO.Compression;
using System.IO.Pipelines;
using System.Linq;
using System.Net.Http;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Text.Json;
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

		private readonly BilibiliApiClient _apiClient;

		protected string? Host;
		protected ushort Port;
		protected abstract string Server { get; }
		private string? _token;
		private long _uid;
		private const short ReceiveProtocolVersion = 2;
		private const short SendProtocolVersion = 1;
		private const int SendHeaderLength = 16;

		private const string DefaultHost = @"broadcastlv.chat.bilibili.com";
		protected abstract ushort DefaultPort { get; }

		protected abstract bool ClientConnected { get; }

		private IDisposable? _heartBeatTask;
		private static readonly TimeSpan HeartBeatInterval = TimeSpan.FromSeconds(30);

		private CancellationTokenSource? _cts;
		private readonly CompositeDisposable _disposableServices = new();

		protected const int BufferSize = 1024;

		private string LogHeader => $@"[{RoomId}]";

		private static readonly TimeSpan GetServerInterval = TimeSpan.FromSeconds(20);
		private DateTime _lastGetServerSuccess;

		protected DanmuClientBase(ILogger logger, BilibiliApiClient apiClient)
		{
			_logger = logger;
			_apiClient = apiClient;
		}

		protected abstract ushort GetPort(HostServerList server);

		protected abstract IDisposable CreateClient();

		protected abstract ValueTask<IDuplexPipe> ClientHandshakeAsync(CancellationToken token);

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

				if (DateTime.Now - _lastGetServerSuccess < GetServerInterval)
				{
					_logger.LogDebug(@"{0} 跳过获取弹幕服务器", LogHeader);
					return;
				}
				_lastGetServerSuccess = DateTime.Now;

				var conf = await _apiClient.GetDanmuConfAsync(RoomId, token);
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
					_uid = await _apiClient.GetUidAsync(token);
				}
				catch
				{
					// ignored
				}
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, @"{0} 获取弹幕服务器失败", LogHeader);
			}
			finally
			{
				if (string.IsNullOrEmpty(_token) || string.IsNullOrWhiteSpace(Host))
				{
					_logger.LogWarning(@"{0} 使用默认弹幕服务器", LogHeader);
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
				_logger.LogInformation(@"{0} 不再连接弹幕服务器", LogHeader);
			}
		}

		private bool IsAuthSuccess(in DanmuPacket packet)
		{
			try
			{
				if (packet.Operation == Operation.AuthReply)
				{
					var json = Encoding.UTF8.GetString(packet.Body);
					_logger.LogDebug(@"{0} 进房回应 {1}", LogHeader, json);
					if (json == @"{""code"":0}")
					{
						return true;
					}
					var jsonElement = JsonSerializer.Deserialize<JsonElement>(json);
					var code = jsonElement.GetProperty(@"code").GetInt64();
					return code == 0;
				}

				return false;
			}
			catch
			{
				return false;
			}
		}

		private async ValueTask ConnectWithRetryAsync(CancellationToken token)
		{
			while (!ClientConnected && !token.IsCancellationRequested)
			{
				await GetServerAsync(token);

				_logger.LogInformation(@"{0} 正在连接弹幕服务器 {1}", LogHeader, Server);

				var pipe = await ConnectAsync(token);
				if (pipe is null)
				{
					Close();
					await WaitAsync(token);
					continue;
				}

				_logger.LogInformation(@"{0} 连接弹幕服务器成功", LogHeader);

				Received.Take(1).Subscribe(packet =>
				{
					if (IsAuthSuccess(packet))
					{
						_logger.LogInformation(@"{0} 进房成功", LogHeader);
					}
					else
					{
						_logger.LogWarning(@"{0} 进房失败", LogHeader);
						Close();
					}
				});

				ProcessDanMuAsync(pipe.Input, token).Forget();
				break;
			}
		}

		private async ValueTask<IDuplexPipe?> ConnectAsync(CancellationToken token)
		{
			try
			{
				var client = CreateClient();
				_disposableServices.Add(client);

				var pipe = await ClientHandshakeAsync(token);
				var writer = pipe.Output;

				await SendAuthAsync(writer, token);

				_heartBeatTask = Observable.Interval(HeartBeatInterval)
#pragma warning disable VSTHRD101
					.Subscribe(async _ => await SendHeartbeatAsync(writer, token));
#pragma warning restore VSTHRD101

				_disposableServices.Add(_heartBeatTask);

				return pipe;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, @"{0} 连接弹幕服务器错误", LogHeader);
				return null;
			}
		}

		private static async ValueTask SendDataAsync(PipeWriter writer, Operation operation, string body, CancellationToken token)
		{
			var memory = writer.GetMemory(SendHeaderLength + Encoding.UTF8.GetMaxByteCount(body.Length));

			var dataLength = Encoding.UTF8.GetBytes(body, memory.Span.Slice(SendHeaderLength));
			var packet = new DanmuPacket
			{
				PacketLength = dataLength + SendHeaderLength,
				HeaderLength = SendHeaderLength,
				ProtocolVersion = SendProtocolVersion,
				Operation = operation,
				SequenceId = 1
			};
			packet.HeaderTo(memory.Span);

			writer.Advance(packet.PacketLength);
			await writer.FlushAsync(token);
		}

		private async ValueTask SendAuthAsync(PipeWriter writer, CancellationToken token)
		{
			string json;
			if (string.IsNullOrEmpty(_token))
			{
				json = @$"{{""roomid"":{RoomId},""uid"":{_uid},""protover"":{ReceiveProtocolVersion}}}";
			}
			else
			{
				json = @$"{{""roomid"":{RoomId},""uid"":{_uid},""protover"":{ReceiveProtocolVersion},""key"":""{_token}""}}";
			}
			_logger.LogDebug(@"{0} AuthJson: {1}", LogHeader, json);
			await SendDataAsync(writer, Operation.Auth, json, token);
		}

		private async ValueTask SendHeartbeatAsync(PipeWriter writer, CancellationToken token)
		{
			try
			{
				_logger.LogDebug(@"{0} 发送心跳包", LogHeader);
				await SendDataAsync(writer, Operation.Heartbeat, string.Empty, token);
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, @"{0} 心跳包发送失败", LogHeader);
				Close();
			}
		}

		private async ValueTask ProcessDanMuAsync(PipeReader reader, CancellationToken token)
		{
			try
			{
				await ReadPipeAsync(reader, token);
				_logger.LogWarning(@"{0} 弹幕服务器不再发送弹幕，尝试重连...", LogHeader);
			}
			catch (OperationCanceledException)
			{
				_logger.LogInformation(@"{0} 不再连接弹幕服务器", LogHeader);
				return;
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, @"{0} 弹幕服务器连接被断开，尝试重连...", LogHeader);
			}

			Close();
			await WaitAsync(token);
			await ConnectWithRetryAsync(token);
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

							if (!success)
							{
								break;
							}

							await ProcessDanMuPacketAsync(packet, token);
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
					var stream = packet.Body.Slice(2).AsStream(); // Drop header
					await using var deflate = new DeflateStream(stream, CompressionMode.Decompress, false);
					var reader = PipeReader.Create(deflate);

					await ReadPipeAsync(reader, token);

					break;
				}
				default:
				{
					_logger.LogWarning(@"{0} 弹幕协议不支持。Version: {1}", LogHeader, packet.ProtocolVersion);
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
					var reader = new SequenceReader<byte>(packet.Body);
					reader.TryReadBigEndian(out int num);
					_logger.LogDebug(@"{0} 收到弹幕[{1}] 人气值: {2}", LogHeader, packet.Operation, num);
					break;
				}
				case Operation.SendMsgReply:
				case Operation.AuthReply:
				{
					_logger.LogDebug(@"{0} 收到弹幕[{1}]:{2}", LogHeader, packet.Operation, Encoding.UTF8.GetString(packet.Body));
					break;
				}
				default:
				{
					_logger.LogDebug(@"{0} 收到弹幕[{1}]", LogHeader, packet.Operation);
					break;
				}
			}
#endif
			_danMuSubj.OnNext(packet);
		}

		private void Close()
		{
			_disposableServices.Clear();
		}

		public virtual ValueTask StopAsync()
		{
			_cts?.Cancel();
			Close();
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
			_disposableServices.Dispose();
		}
	}
}
