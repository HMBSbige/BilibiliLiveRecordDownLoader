using BilibiliLiveRecordDownLoader.FlvProcessor.Enums;
using BilibiliLiveRecordDownLoader.FlvProcessor.Interfaces;
using BilibiliLiveRecordDownLoader.FlvProcessor.Models;
using BilibiliLiveRecordDownLoader.FlvProcessor.Models.FlvTagHeaders;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliLiveRecordDownLoader.FlvProcessor.Clients
{
    public class FlvMerger1 : IFlvMerger
    {
        private readonly ILogger _logger;
        private static readonly ArrayPool<byte> ArrayPool = ArrayPool<byte>.Shared;

        private long _fileSize;
        private long _current;
        private long _last;

        private readonly BehaviorSubject<double> _progressUpdated = new BehaviorSubject<double>(0.0);
        public IObservable<double> ProgressUpdated => _progressUpdated.AsObservable();

        private readonly BehaviorSubject<double> _currentSpeed = new BehaviorSubject<double>(0.0);
        public IObservable<double> CurrentSpeed => _currentSpeed.AsObservable();

        public int BufferSize { get; set; } = 4096;

        public bool IsAsync { get; set; } = false;

        private readonly List<string> _files = new List<string>();
        public IEnumerable<string> Files => _files;

        public FlvMerger1(ILogger<FlvMerger1> logger)
        {
            _logger = logger;
        }

        public void Add(string path)
        {
            _files.Add(path);
        }

        public void AddRange(IEnumerable<string> path)
        {
            _files.AddRange(path);
        }

        public async ValueTask MergeAsync(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            _fileSize = Files.Sum(file => new FileInfo(file).Length);

            var sw = Stopwatch.StartNew();
            using var monitorSpeed = Observable.Interval(TimeSpan.FromSeconds(1)).Subscribe(_ =>
            {
                var last = Interlocked.Read(ref _last);
                _currentSpeed.OnNext(last / sw.Elapsed.TotalSeconds);
                sw.Restart();
                Interlocked.Add(ref _last, -last);
            });

            await using var outFile = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, IsAsync);

            var header = new FlvHeader();
            var metadata = new FlvTagHeader();

            await using (var f0 = new FileStream(Files.First(), FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize))
            {
                var headerBuffer = ArrayPool.Rent(header.Size);
                try
                {
                    var memory = headerBuffer.AsMemory(0, header.Size);

                    // 读 Header
                    f0.Read(memory.Span);
                    header.Read(memory.Span);
                    _logger.LogDebug($@"{header.Signature} {header.Version} {header.Flags} {header.HeaderSize}");

                    // 写 header
                    WriteWithProgress(outFile, memory.Span, token);
                }
                finally
                {
                    ArrayPool.Return(headerBuffer);
                }

                var metadataBuffer = ArrayPool.Rent(metadata.Size);
                try
                {
                    var memory = headerBuffer.AsMemory(0, metadata.Size);

                    // 读 Metadata
                    f0.Read(memory.Span);
                    metadata.Read(memory.Span);

                    if (metadata.PayloadInfo.PacketType == PacketType.AMF_Metadata)
                    {
                        // 写 MetaData
                        WriteWithProgress(outFile, memory.Span, token);
                        CopyFixedSize(f0, outFile, (int)metadata.PayloadInfo.PayloadSize, token);
                    }
                    else
                    {
                        //TODO: 写自定义 MetaData
                        _logger.LogWarning(@"First packet is not a metadata packet");
                    }
                }
                finally
                {
                    ArrayPool.Return(metadataBuffer);
                }
            }

            var timestamp = 0u;
            var allRead = 0L;
            foreach (var file in Files)
            {
                var currentTimestamp = 0u;
                var read = 0L;

                await using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
                var length = fs.Length;

                fs.Seek(header.Size, SeekOrigin.Begin);

                while (read < length)
                {
                    var tagHeader = new FlvTagHeader();
                    var tagHeaderBuffer = ArrayPool.Rent(tagHeader.Size);
                    var payloadLength = tagHeader.PayloadInfo.PayloadSize;
                    try
                    {
                        var memory = tagHeaderBuffer.AsMemory(0, tagHeader.Size);
                        fs.Read(memory.Span);
                        tagHeader.Read(memory.Span);

                        if (tagHeader.PayloadInfo.PacketType != PacketType.AMF_Metadata)
                        {
                            // 重写时间戳
                            currentTimestamp = tagHeader.Timestamp.Data;
                            tagHeader.Timestamp.Data += timestamp;

                            // 写 tag header
                            WriteWithProgress(outFile, tagHeader.ToMemory(memory).Span, token);

                            // 复制 Payload
                            CopyFixedSize(fs, outFile, (int)payloadLength, token);
                        }
                    }
                    finally
                    {
                        ArrayPool.Return(tagHeaderBuffer);
                    }

                    read += tagHeader.Size + payloadLength;
                }

                timestamp += currentTimestamp;
                allRead += read;
                Interlocked.Exchange(ref _current, allRead);
            }

            FixDuration(outFile, header.Size, (int)metadata.PayloadInfo.PayloadSize + metadata.Size, timestamp / 1000.0);
        }

        private void CopyFixedSize(Stream source, Stream dst, int size, CancellationToken token)
        {
            var buffer = ArrayPool.Rent(Math.Min(BufferSize, size));
            try
            {
                var span = buffer.AsSpan();
                while (size > 0)
                {
                    var shouldRead = Math.Min(size, span.Length);

                    var read = source.Read(span.Slice(0, shouldRead));
                    if (read <= 0)
                    {
                        break;
                    }

                    size -= read;
                    WriteWithProgress(dst, span.Slice(0, read), token);
                }
            }
            finally
            {
                ArrayPool.Return(buffer);
            }
        }

        private void WriteWithProgress(Stream dst, ReadOnlySpan<byte> buffer, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            dst.Write(buffer);
            ReportProgress(buffer.Length);
        }

        private void ReportProgress(long length)
        {
            Interlocked.Add(ref _last, length);
            Interlocked.Add(ref _current, length);
            _progressUpdated.OnNext(Interlocked.Read(ref _current) / (double)_fileSize);
        }

        /// <summary>
        /// {sizeof("duration")}{duration}{double}
        /// </summary>
        private static readonly Memory<byte> 特征duration = new byte[] { 0x08, 0x64, 0x75, 0x72, 0x61, 0x74, 0x69, 0x6F, 0x6E, 0x00 };

        private void FixDuration(Stream file, int offset, int size, double duration)
        {
            file.Seek(offset, SeekOrigin.Begin);

            const int durationSize = sizeof(double);

            var b = ArrayPool.Rent(size + durationSize);
            try
            {
                var span = b.AsSpan(0, size);

                file.Read(span);

                var index = span.IndexOf(特征duration.Span);
                if (index < 0)
                {
                    _logger.LogWarning(@"找不到 duration 字段");
                    return;
                }

                var i = offset + span.IndexOf(特征duration.Span) + 特征duration.Length;

                var outBytes = b.AsSpan(size, durationSize);

                // TODO:.NET 5.0
                BinaryPrimitives.TryWriteInt64BigEndian(outBytes, BitConverter.DoubleToInt64Bits(duration));

                file.Seek(i, SeekOrigin.Begin);
                WriteWithProgress(file, outBytes, default);
            }
            finally
            {
                ArrayPool.Return(b);
            }
        }

        public ValueTask DisposeAsync()
        {
            _progressUpdated.OnCompleted();
            _currentSpeed.OnCompleted();

            return default;
        }
    }
}