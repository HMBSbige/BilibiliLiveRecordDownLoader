using BilibiliApi.Enums;
using BilibiliApi.Model.Danmu;
using BilibiliApi.Model.DanmuConf;
using BilibiliLiveRecordDownLoader.Shared;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.IO.Pipelines;
using System.Linq;
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

        private readonly Subject<DanmuPacket> _danMuSubj = new Subject<DanmuPacket>();
        public IObservable<DanmuPacket> Received => _danMuSubj.AsObservable();

        protected string Host;
        protected ushort Port;
        protected abstract string Server { get; }
        private string _token;

        private const string DefaultHost = @"broadcastlv.chat.bilibili.com";
        protected abstract ushort DefaultPort { get; }
        private const string DefaultToken = @"";

        protected abstract bool ClientConnected { get; }

        private IDisposable _heartBeatTask;

        private CancellationTokenSource _cts;

        private const int BufferSize = 1024;

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

            _cts = new CancellationTokenSource();

            await GetServerAsync(_cts.Token);

            await ConnectWithRetryAsync(_cts.Token);
        }

        private async ValueTask GetServerAsync(CancellationToken token)
        {
            try
            {
                using var client = new BililiveApiClient();
                var conf = await client.GetDanmuConf(RoomId, token);
                _token = conf.data.token;

                Host = conf.data.host_server_list.First().host;
                Port = GetPort(conf.data.host_server_list.First());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, @"获取弹幕服务器地址失败，尝试使用默认服务器地址");
            }
            finally
            {
                if (string.IsNullOrWhiteSpace(Host))
                {
                    Host = DefaultHost;
                }

                if (Port == 0)
                {
                    Port = DefaultPort;
                }

                _token ??= DefaultToken;
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
                _logger.LogInformation($@"[{RoomId}] 不再连接弹幕服务器");
            }
        }

        private async ValueTask ConnectWithRetryAsync(CancellationToken token)
        {
            while (!ClientConnected && !token.IsCancellationRequested)
            {
                _logger.LogInformation($@"[{RoomId}] 正在连接弹幕服务器 {Server}");

                if (!await ConnectAsync(token))
                {
                    await WaitAsync(token);
                    continue;
                }

                _logger.LogInformation($@"[{RoomId}] 连接弹幕服务器成功");

                ProcessDanMuAsync(token).NoWarning();

                break;
            }
        }

        private async ValueTask<bool> ConnectAsync(CancellationToken token)
        {
            try
            {
                await ClientHandshakeAsync(token);

                await AuthAsync(token);

                _heartBeatTask = Observable.Interval(TimeSpan.FromSeconds(30))
                    .Subscribe(_ => SendHeartbeatAsync(token).NoWarning());

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $@"[{RoomId}] 连接弹幕服务器错误");
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

        private async ValueTask AuthAsync(CancellationToken token)
        {
            var json = @$"{{""roomid"":{RoomId},""uid"":0,""protover"":2,""key"":""{_token}""}}";
            await SendDataAsync(Operation.Auth, json, token);
        }

        private async ValueTask SendHeartbeatAsync(CancellationToken token)
        {
            try
            {
                _logger.LogDebug(@"发送心跳包");
                await SendDataAsync(Operation.Heartbeat, string.Empty, token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, @"心跳包发送失败");
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
                _logger.LogInformation($@"[{RoomId}] 不再连接弹幕服务器");
            }
            catch (Exception ex)
            {
                ResetClient();

                _logger.LogWarning(ex, $@"[{RoomId}] 弹幕服务器连接被断开，尝试重连...");
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

                    _logger.LogDebug($@"收到 {bytesRead} 字节");

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

                    var packet = new DanmuPacket();
                    buffer = packet.ReadDanMu(buffer);

                    await ProcessDanMuPacketAsync(packet, token);

                    reader.AdvanceTo(buffer.Start, buffer.End);

                    if (result.IsCompleted)
                    {
                        break;
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
            if (packet.PacketLength >= 16)
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
                        await ms.WriteAsync(packet.Body.Slice(2), token); // Drop header
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
                        _logger.LogWarning($@"弹幕协议不支持。Version: {packet.ProtocolVersion}");
                        break;
                    }
                }
            }
        }

        private void EmitDanmu(in DanmuPacket packet)
        {
            if (packet == null)
            {
                return;
            }
#if DEBUG
            switch (packet.Operation)
            {
                case Operation.HeartbeatReply:
                {
                    _logger.LogDebug($@"收到弹幕[{packet.Operation}] 人气值: {BinaryPrimitives.ReadUInt32BigEndian(packet.Body.Span)}");
                    break;
                }
                case Operation.SendMsgReply:
                case Operation.AuthReply:
                {
                    _logger.LogDebug(@"收到弹幕[{0}]:{1}", packet.Operation, Encoding.UTF8.GetString(packet.Body.Span));
                    break;
                }
                default:
                {
                    _logger.LogDebug($@"收到弹幕[{packet.Operation}]");
                    break;
                }
            }
#endif
            _danMuSubj.OnNext(packet);
        }

        protected virtual void ResetClient()
        {
            _heartBeatTask?.Dispose();
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
        }
    }
}