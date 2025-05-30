using BilibiliApi.Enums;
using BilibiliApi.Model.Danmu;
using BilibiliApi.Model.Danmu.DanmuBody;
using BilibiliApi.Model.DanmuConf;
using BilibiliLiveRecordDownLoader.Shared.Utils;
using Microsoft;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Pipelines.Extensions;
using System.Buffers;
using System.IO.Compression;
using System.IO.Pipelines;
using System.Net;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BilibiliApi.Clients;

public abstract class DanmuClientBase(ILogger<DanmuClientBase> logger, BilibiliApiClient apiClient, IDistributedCache cacheService)
	: IDanmuClient
{
	protected readonly BilibiliApiClient ApiClient = apiClient;

	public long RoomId { get; set; }

	public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(2);

	private static readonly TimeSpan HeartBeatInterval = TimeSpan.FromSeconds(30);

	private string DanmuServerCacheKey => @"🤣DanmuClient.Servers." + RoomId + _uid;

	private static readonly DistributedCacheEntryOptions DanmuServerCacheOptions = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) };

	private const short ReceiveProtocolVersion = 3;
	private const short SendProtocolVersion = 1;
	private const int SendHeaderLength = 16;

	private readonly Subject<DanmuPacket> _danMuSubj = new();

	public IObservable<DanmuPacket> Received => _danMuSubj.AsObservable();

	protected abstract string Server { get; }

	protected string? Host;
	protected ushort Port;
	private string? _token;
	private long _uid;

	private const string DefaultHost = @"broadcastlv.chat.bilibili.com";

	protected abstract ushort DefaultPort { get; }

	private CancellationTokenSource? _cts;
	private readonly CompositeDisposable _disposableServices = new();

	protected abstract ushort GetPort(HostServerList server);

	protected abstract IDisposable CreateClient();

	protected abstract ValueTask<IDuplexPipe> ClientHandshakeAsync(CancellationToken cancellationToken);

	public virtual async ValueTask StartAsync()
	{
		Verify.NotDisposed(this);

		Stop();

		_cts = new CancellationTokenSource();

		using IDisposable? _ = logger.BeginScope($@"开始连接弹幕服务器 {{{LoggerProperties.RoomIdPropertyName}}}", RoomId);
		await ConnectWithRetryAsync(_cts.Token);
	}

	private async ValueTask ConnectWithRetryAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();

				await GetServerAsync(cancellationToken);

				logger.LogInformation(@"正在连接弹幕服务器 {server}", Server);

				IDuplexPipe? pipe = await ConnectAsync(cancellationToken);

				if (pipe is not null)
				{
					logger.LogInformation(@"连接弹幕服务器成功");

					IDisposable receiveAuthTask = Received.Take(1).Subscribe(packet =>
					{
						if (IsAuthSuccess())
						{
							logger.LogInformation(@"进房成功");
						}
						else
						{
							logger.LogWarning(@"进房失败");
							Close();
						}

						return;

						bool IsAuthSuccess()
						{
							try
							{
								if (packet.Operation is not Operation.AuthReply)
								{
									return false;
								}

								string json = Encoding.UTF8.GetString(packet.Body);
								logger.LogDebug(@"进房回应 {jsonString}", json);

								if (json is """{"code":0}""")
								{
									return true;
								}

								JsonElement root = JsonSerializer.Deserialize<JsonElement>(json);
								return root.TryGetProperty(@"code", out JsonElement codeElement) && codeElement.TryGetInt64(out long code) && code is 0;
							}
							catch
							{
								return false;
							}
						}
					});
					_disposableServices.Add(receiveAuthTask);

					await ProcessDanMuAsync(pipe.Input);
				}

				Close();
				await Task.Delay(RetryInterval, cancellationToken);
			}
		}
		catch (Exception) when (cancellationToken.IsCancellationRequested)
		{
			logger.LogInformation(@"不再连接弹幕服务器");
		}
		catch (Exception ex)
		{
			logger.LogError(ex, @"连接弹幕服务器发生未知错误");
		}
		finally
		{
			Close();
		}

		return;

		async ValueTask ProcessDanMuAsync(PipeReader reader)
		{
			try
			{
				await ReadPipeAsync(reader, cancellationToken);
				logger.LogWarning(@"弹幕服务器不再发送弹幕，尝试重连...");
			}
			catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
			{
				logger.LogWarning(ex, @"弹幕服务器连接被断开，尝试重连...");
			}
		}
	}

	/// <summary>
	/// 获取弹幕服务器
	/// </summary>
	private async ValueTask GetServerAsync(CancellationToken cancellationToken)
	{
		try
		{
			_uid = await ApiClient.GetUidAsync(cancellationToken);
		}
		catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
		{
			logger.LogWarning(ex, @"获取 uid 失败");
		}

		_token = default;

		try
		{
			DanmuConfData? danmuInfoData;
			HostServerList? server;

			byte[]? cacheBytes = await cacheService.GetAsync(DanmuServerCacheKey, cancellationToken);

			if (cacheBytes is not null)
			{
				danmuInfoData = JsonSerializer.Deserialize<DanmuConfData>(cacheBytes);
				Assumes.NotNull(danmuInfoData?.host_list);

				server = string.IsNullOrEmpty(Host) ? danmuInfoData.host_list.First() : danmuInfoData.host_list[RandomNumberGenerator.GetInt32(danmuInfoData.host_list.Length)];
			}
			else
			{
				Host = default;

				DanmuConfMessage? conf = await ApiClient.GetDanmuConfAsync(RoomId, cancellationToken);

				if (conf?.code is -352)
				{
					logger.LogWarning(@"获取弹幕服务器失败：{message}", @"被风控限制，尝试使用匿名请求");
					BilibiliApiClient anonymous = new(HttpClientUtils.BuildClientForBilibili(
						default,
						default,
						new SocketsHttpHandler
						{
							AutomaticDecompression = DecompressionMethods.Brotli,
							UseCookies = false,
							UseProxy = WebRequest.DefaultWebProxy is not null
						})
					);
					conf = await anonymous.GetDanmuConfAsync(RoomId, cancellationToken);
					_uid = default;
				}

				if (conf?.code is not 0 && !string.IsNullOrEmpty(conf?.message))
				{
					logger.LogError(@"获取弹幕服务器失败：{message}", conf.message);
					return;
				}

				danmuInfoData = conf?.data;

				if (string.IsNullOrEmpty(danmuInfoData?.token) || danmuInfoData.host_list is null || danmuInfoData.host_list.Length is 0)
				{
					logger.LogError(@"获取弹幕服务器失败：返回信息中未包含服务器地址");
					return;
				}

				await cacheService.SetAsync(DanmuServerCacheKey, JsonSerializer.SerializeToUtf8Bytes(danmuInfoData), DanmuServerCacheOptions, cancellationToken);
				server = danmuInfoData.host_list.First();
			}

			Host = server.host;
			Port = GetPort(server);
			_token = danmuInfoData.token;
		}
		catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
		{
			logger.LogWarning(ex, @"获取弹幕服务器失败");
		}
		finally
		{
			if (!cancellationToken.IsCancellationRequested)
			{
				if (string.IsNullOrEmpty(_token) || string.IsNullOrEmpty(Host))
				{
					logger.LogWarning(@"使用默认弹幕服务器");
					Host = DefaultHost;
					Port = DefaultPort;
					_token = default;
				}
			}
		}
	}

	private async ValueTask<IDuplexPipe?> ConnectAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		try
		{
			IDisposable client = CreateClient();
			_disposableServices.Add(client);

			IDuplexPipe pipe = await ClientHandshakeAsync(cancellationToken);

			await SendAuthAsync(pipe.Output);

			IDisposable heartBeatTask = Observable.Interval(HeartBeatInterval).SelectMany(_ => SendHeartBeatAsync(pipe.Output)).Subscribe();
			_disposableServices.Add(heartBeatTask);

			return pipe;
		}
		catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
		{
			logger.LogError(ex, @"连接弹幕服务器错误");
			return null;
		}

		async ValueTask SendAuthAsync(PipeWriter writer)
		{
			AuthDanmu authBody = new()
			{
				RoomId = RoomId,
				UserId = _uid,
				ProtocolVersion = ReceiveProtocolVersion,
				Token = _token
			};
			string json = JsonSerializer.Serialize(authBody, AuthDanmuJsonSerializerContext.Default.AuthDanmu);

			logger.LogDebug(@"AuthJson: {jsonString}", json);
			await SendDataAsync(writer, Operation.Auth, json, cancellationToken);
		}

		async Task<Unit> SendHeartBeatAsync(PipeWriter writer)
		{
			try
			{
				logger.LogDebug(@"发送心跳包");
				await SendDataAsync(writer, Operation.Heartbeat, string.Empty, cancellationToken);
			}
			catch (Exception ex)
			{
				logger.LogWarning(ex, @"心跳包发送失败");
				Close();
			}

			return default;
		}
	}

	private static async ValueTask SendDataAsync(PipeWriter writer, Operation operation, string body, CancellationToken cancellationToken)
	{
		Memory<byte> memory = writer.GetMemory(SendHeaderLength + Encoding.UTF8.GetMaxByteCount(body.Length));

		int dataLength = Encoding.UTF8.GetBytes(body, memory.Span[SendHeaderLength..]);
		DanmuPacket packet = new()
		{
			PacketLength = dataLength + SendHeaderLength,
			HeaderLength = SendHeaderLength,
			ProtocolVersion = SendProtocolVersion,
			Operation = operation,
			SequenceId = 1
		};
		packet.GetHeaderBytes(memory.Span);

		writer.Advance(packet.PacketLength);
		await writer.FlushAsync(cancellationToken);
	}

	private async ValueTask ReadPipeAsync(PipeReader reader, CancellationToken cancellationToken)
	{
		try
		{
			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();

				ReadResult result = await reader.ReadAsync(cancellationToken);
				ReadOnlySequence<byte> buffer = result.Buffer;

				try
				{
					while (buffer.Length >= 16)
					{
						DanmuPacket packet = new();
						bool success = packet.ReadDanMu(ref buffer);

						if (!success)
						{
							break;
						}

						await ProcessDanMuPacketAsync(packet, cancellationToken);
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

	private async ValueTask ProcessDanMuPacketAsync(DanmuPacket packet, CancellationToken cancellationToken)
	{
		switch (packet.ProtocolVersion)
		{
			case 0:
			case 1:
			{
				EmitDanmu();
				break;
			}
			case 2:
			{
				Stream stream = packet.Body.Slice(2).AsStream();// Drop header
				await using DeflateStream deflate = new(stream, CompressionMode.Decompress, false);
				PipeReader reader = PipeReader.Create(deflate);
				await ReadPipeAsync(reader, cancellationToken);

				break;
			}
			case 3:
			{
				await using BrotliStream brotli = new(packet.Body.AsStream(), CompressionMode.Decompress, false);
				PipeReader reader = PipeReader.Create(brotli);
				await ReadPipeAsync(reader, cancellationToken);

				break;
			}
			default:
			{
				logger.LogWarning(@"弹幕协议不支持。Version: {protocolVersion}", packet.ProtocolVersion);
				break;
			}
		}

		return;

		void EmitDanmu()
		{
			_danMuSubj.OnNext(packet);
		}
	}

	private void Close()
	{
		_disposableServices.Clear();
	}

	private void Stop()
	{
		if (_cts is not null)
		{
			_cts.Cancel();
			_cts.Dispose();
		}

		Close();
	}

	public bool IsDisposed { get; private set; }

	public void Dispose()
	{
		if (IsDisposed)
		{
			return;
		}

		IsDisposed = true;

		_danMuSubj.OnCompleted();

		Stop();

		_disposableServices.Dispose();

		GC.SuppressFinalize(this);
	}
}
