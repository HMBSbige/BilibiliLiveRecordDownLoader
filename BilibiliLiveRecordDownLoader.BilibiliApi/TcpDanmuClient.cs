using BilibiliApi.Model.Danmu;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.IO.Pipelines;
using System.Linq;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliApi
{
    public class TcpDanmuClient : IDanmuClient
    {
        private readonly ILogger _logger;

        public long RoomId { get; set; }

        public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(10);

        private readonly Subject<Danmu> _danMuSubj = new Subject<Danmu>();
        public IObservable<Danmu> Received => _danMuSubj.AsObservable();

        private string _host;
        private ushort _port;
        private string _token;

        private const string DefaultHost = @"broadcastlv.chat.bilibili.com";
        private const ushort DefaultPort = 2243;
        private const string DefaultToken = @"";

        private TcpClient _client;
        private bool TcpConnected => _client?.Connected ?? false;

        private IDisposable _heartBeatTask;

        private CancellationTokenSource _cts;

        private static readonly ArrayPool<byte> BytePool = ArrayPool<byte>.Shared;

        private const int BufferSize = 1024;

        public TcpDanmuClient(ILogger logger)
        {
            _logger = logger;
        }

        public async ValueTask StartAsync()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(TcpDanmuClient));
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

                _host = conf.data.host_server_list.First().host;
                _port = conf.data.host_server_list.First().port;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, @"获取弹幕服务器地址失败，尝试使用默认服务器地址");
            }
            finally
            {
                if (string.IsNullOrWhiteSpace(_host))
                {
                    _host = DefaultHost;
                }

                if (_port == 0)
                {
                    _port = DefaultPort;
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
            while (!TcpConnected && !token.IsCancellationRequested)
            {
                _logger.LogInformation($@"[{RoomId}] 正在连接弹幕服务器...");

                if (!await ConnectAsync(token))
                {
                    await WaitAsync(token);
                    continue;
                }

                _logger.LogInformation($@"[{RoomId}] 连接弹幕服务器成功");

                ProcessDanMuAsync(_client.GetStream(), token).NoWarning();

                break;
            }
        }

        private async ValueTask<bool> ConnectAsync(CancellationToken token)
        {
            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(_host, _port);
                var netStream = _client.GetStream();

                await JoinChannelAsync(netStream, token);

                _heartBeatTask = Observable.Interval(TimeSpan.FromSeconds(30))
                    .Subscribe(_ => SendHeartbeatAsync(netStream, token).NoWarning());

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $@"[{RoomId}] 连接弹幕服务器错误");
                return false;
            }
        }

        private static async ValueTask SendDataAsync(Stream stream, int action, string body, CancellationToken token)
        {
            var data = Encoding.UTF8.GetBytes(body);
            var packet = new DanmuPacket
            {
                PacketLength = data.Length + 16,
                HeaderLength = 16,
                ProtocolVersion = 1,
                Operation = action,
                SequenceId = 1,
                Body = data
            };
            var bytes = BytePool.Rent(packet.PacketLength);
            try
            {
                var buffer = packet.ToMemory(bytes);

                await stream.WriteAsync(buffer, token);
                await stream.FlushAsync(token);
            }
            finally
            {
                BytePool.Return(bytes);
            }
        }

        private async ValueTask JoinChannelAsync(Stream stream, CancellationToken token)
        {
            var json = @$"{{""roomid"":{RoomId},""uid"":0,""protover"":2,""key"":""{_token}""}}";
            await SendDataAsync(stream, 7, json, token);
        }

        private async ValueTask SendHeartbeatAsync(Stream stream, CancellationToken token)
        {
            try
            {
                _logger.LogDebug(@"发送心跳包");
                await SendDataAsync(stream, 2, string.Empty, token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, @"心跳包发送失败");
            }
        }

        private async ValueTask ProcessDanMuAsync(Stream stream, CancellationToken token)
        {
            try
            {
                var pipe = new Pipe();
                var writing = FillPipeAsync(stream, pipe.Writer, token);
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

        private async ValueTask FillPipeAsync(Stream stream, PipeWriter writer, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var memory = writer.GetMemory(BufferSize);

                    var bytesRead = await stream.ReadAsync(memory, token);

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

                        var header = BytePool.Rent(4);
                        try
                        {
                            while (true)
                            {
                                var headerLength = await deflate.ReadAsync(header.AsMemory(0, 4), token);

                                if (headerLength < 4)
                                {
                                    break;
                                }

                                var packetLength = BinaryPrimitives.ReadInt32BigEndian(header);
                                var remainSize = packetLength - headerLength;
                                var subBuffer = BytePool.Rent(remainSize);
                                try
                                {
                                    await deflate.ReadAsync(subBuffer.AsMemory(0, remainSize), token);

                                    var subPacket = new DanmuPacket { PacketLength = packetLength };
                                    subPacket.ReadDanMu(subBuffer);

                                    await ProcessDanMuPacketAsync(subPacket, token);
                                }
                                finally
                                {
                                    BytePool.Return(subBuffer);
                                }
                            }
                        }
                        finally
                        {
                            BytePool.Return(header);
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
            switch (packet.Operation)
            {
                case 3:
                {
                    // 心跳回应
                    _logger.LogDebug($@"收到弹幕[{packet.Operation}] 人气值: {BinaryPrimitives.ReadUInt32BigEndian(packet.Body.Span)}");
                    break;
                }
                case 5:
                {
                    _logger.LogDebug($@"收到弹幕[{packet.Operation}]: {Encoding.UTF8.GetString(packet.Body.Span)}");
                    break;
                }
                case 8:
                {
                    // 进房回应
                    _logger.LogDebug($@"收到弹幕[{packet.Operation}]: {Encoding.UTF8.GetString(packet.Body.Span)}");
                    break;
                }
                default:
                {
                    _logger.LogDebug($@"收到弹幕[{packet.Operation}]");
                    break;
                }
            }
        }

        private void ResetClient()
        {
            _client?.Dispose();
            _heartBeatTask?.Dispose();
        }

        public ValueTask StopAsync()
        {
            _cts?.Cancel();
            ResetClient();
            return default;
        }

        private volatile bool _isDisposed;

        public async ValueTask DisposeAsync()
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