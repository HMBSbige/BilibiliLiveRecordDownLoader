using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliLiveRecordDownLoader.FlvProcessor
{
    public class FlvMerger
    {
        private const int TagHeaderSize = 4 + 1 + 3 + 3 + 1 + 3;

        public int BufferSize { get; set; } = 4 * 1024;

        public List<string> Files { get; }

        private static readonly ArrayPool<byte> ArrayPool = ArrayPool<byte>.Shared;

        private static readonly Memory<byte> 特征 = new byte[] { 0x08, 0x64, 0x75, 0x72, 0x61, 0x74, 0x69, 0x6F, 0x6E, 0x00 };

        public FlvMerger()
        {
            Files = new List<string>();
        }

        public void Add(string path)
        {
            Files.Add(path);
        }

        public void AddRange(IEnumerable<string> path)
        {
            Files.AddRange(path);
        }

        #region 同步

        public void Merge(string path)
        {
            using var outFile = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, BufferSize, FileOptions.None);

            // 读 Header
            using var f0 = new FileStream(Files.First(), FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.RandomAccess);
            var headerLength = ReadInt32BigEndian(f0, 5);

            // 写 header
            CopyFixedSize(f0, outFile, headerLength);
            var i = headerLength;

            // 读 MetaData
            var metaDataSize = ReadInt32BigEndian(f0, i + 4);
            if (metaDataSize >> 24 == 0x12)
            {
                metaDataSize &= 0x00FFFFFF;
                // 写 MetaData
                CopyFixedSize(f0, outFile, metaDataSize + TagHeaderSize);
                i += metaDataSize + TagHeaderSize;
            }

            var timestamp = 0u;
            var fileNum = 0u;

            foreach (var fileName in Files)
            {
                var currentTimestamp = 0u;

                using var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.RandomAccess);

                if (fileNum > 0)
                {
                    i = ReadInt32BigEndian(fs, 5);
                }

                while (i + TagHeaderSize < fs.Length)
                {
                    var h = ReadInt32BigEndian(fs, i + 4);
                    var tagSize = (h & 0x00FFFFFF) + TagHeaderSize;

                    if (h >> 24 != 0x12) // 跳过 MetaData
                    {
                        currentTimestamp = GetTimeStamp(fs, i + 8);

                        var buffer = ArrayPool.Rent(8 + 4);
                        try
                        {
                            var span = buffer.AsSpan(0, 8 + 4);

                            fs.Position = i;
                            fs.Read(span);

                            if (fileNum > 0) //不是第一个文件的话，重写时间戳
                            {
                                GetTimeStamp(currentTimestamp + timestamp, span.Slice(8));
                            }

                            outFile.Write(span);
                        }
                        finally
                        {
                            ArrayPool.Return(buffer);
                        }

                        if (fs.Length >= i + tagSize)
                        {
                            CopyFixedSize(fs, outFile, tagSize - 8 - 4);
                        }
                    }
                    i += tagSize;
                }

                i = 0;
                timestamp += currentTimestamp;
                ++fileNum;
            }

            FixDuration(outFile, headerLength, metaDataSize + TagHeaderSize, timestamp / 1000.0);
        }

        private static void FixDuration(Stream file, int offset, int size, double duration)
        {
            file.Position = offset;
            var b = ArrayPool.Rent(size + sizeof(double));
            try
            {
                var span = b.AsSpan(0, size);

                file.Read(span);

                var i = offset + span.IndexOf(特征.Span) + 特征.Length;

                var outBytes = b.AsSpan(size, sizeof(double));
                BitConverter.TryWriteBytes(outBytes, duration);

                if (BitConverter.IsLittleEndian)
                {
                    outBytes.Reverse();
                }

                file.Position = i;
                file.Write(outBytes);
            }
            finally
            {
                ArrayPool.Return(b);
            }
        }

        private void CopyFixedSize(Stream source, Stream dst, int size)
        {
            var buffer = ArrayPool.Rent(Math.Min(BufferSize, size));
            try
            {
                var span = buffer.AsSpan();
                while (true)
                {
                    var shouldRead = Math.Min(size, span.Length);

                    var read = source.Read(span.Slice(0, shouldRead));
                    if (read <= 0)
                    {
                        break;
                    }
                    size -= read;
                    dst.Write(span.Slice(0, read));
                }
            }
            finally
            {
                ArrayPool.Return(buffer);
            }
        }

        private static int ReadInt32BigEndian(Stream fs, int offset)
        {
            var origin = fs.Position;
            fs.Position = offset;

            var buffer = ArrayPool.Rent(4);
            try
            {
                var span = buffer.AsSpan(0, 4);
                fs.Read(span);

                fs.Position = origin;

                return BinaryPrimitives.ReadInt32BigEndian(span);
            }
            finally
            {
                ArrayPool.Return(buffer);
            }
        }

        private static uint GetTimeStamp(Stream fs, int offset)
        {
            var origin = fs.Position;
            fs.Position = offset;
            var buffer = ArrayPool.Rent(4);
            try
            {
                var span = buffer.AsSpan(0, 4);
                fs.Read(span);

                fs.Position = origin;

                var m = BinaryPrimitives.ReadUInt32BigEndian(span);

                return ((m & 0xFF) << 24) | (m >> 8);
            }
            finally
            {
                ArrayPool.Return(buffer);
            }
        }

        private static void GetTimeStamp(uint timeStamp, Span<byte> result)
        {
            var bytes = ArrayPool.Rent(4);
            try
            {
                var span = bytes.AsSpan(0, 4);
                BitConverter.TryWriteBytes(span, timeStamp);

                if (BitConverter.IsLittleEndian)
                {
                    result[0] = span[2];
                    result[1] = span[1];
                    result[2] = span[0];
                    result[3] = span[3];
                }
                else
                {
                    result[0] = span[1];
                    result[1] = span[2];
                    result[2] = span[3];
                    result[3] = span[0];
                }
            }
            finally
            {
                ArrayPool.Return(bytes);
            }
        }

        #endregion

        #region 异步

        public async ValueTask MergeAsync(string path, CancellationToken token)
        {
            await using var outFile = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, BufferSize, FileOptions.None);

            // 读 Header
            await using var f0 = new FileStream(Files.First(), FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.RandomAccess);
            var headerLength = await ReadInt32BigEndianAsync(f0, 5, token);

            // 写 header
            await CopyFixedSizeAsync(f0, outFile, headerLength, token);
            var i = headerLength;

            // 读 MetaData
            var metaDataSize = await ReadInt32BigEndianAsync(f0, i + 4, token);
            if (metaDataSize >> 24 == 0x12)
            {
                metaDataSize &= 0x00FFFFFF;
                // 写 MetaData
                await CopyFixedSizeAsync(f0, outFile, metaDataSize + TagHeaderSize, token);
                i += metaDataSize + TagHeaderSize;
            }

            var timestamp = 0u;
            var fileNum = 0u;

            foreach (var fileName in Files)
            {
                var currentTimestamp = 0u;

                await using var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.RandomAccess);

                if (fileNum > 0)
                {
                    i = await ReadInt32BigEndianAsync(fs, 5, token);
                }

                while (i + TagHeaderSize < fs.Length)
                {
                    var h = await ReadInt32BigEndianAsync(fs, i + 4, token);
                    var tagSize = (h & 0x00FFFFFF) + TagHeaderSize;

                    if (h >> 24 != 0x12) // 跳过 MetaData
                    {
                        currentTimestamp = await GetTimeStampAsync(fs, i + 8, token);

                        var buffer = ArrayPool.Rent(8 + 4);
                        try
                        {
                            var memory = buffer.AsMemory(0, 8 + 4);
                            fs.Position = i;
                            await fs.ReadAsync(memory, token);

                            if (fileNum > 0) //不是第一个文件的话，重写时间戳
                            {
                                GetTimeStamp(currentTimestamp + timestamp, memory.Span.Slice(8));
                            }

                            await outFile.WriteAsync(memory, token);
                        }
                        finally
                        {
                            ArrayPool.Return(buffer);
                        }

                        if (fs.Length >= i + tagSize)
                        {
                            await CopyFixedSizeAsync(fs, outFile, tagSize - 8 - 4, token);
                        }
                    }
                    i += tagSize;
                }

                i = 0;
                timestamp += currentTimestamp;
                ++fileNum;
            }

            await FixDurationAsync(outFile, headerLength, metaDataSize + TagHeaderSize, timestamp / 1000.0, token);
        }

        private static async ValueTask FixDurationAsync(Stream file, int offset, int size, double duration, CancellationToken token)
        {
            file.Position = offset;
            var b = ArrayPool.Rent(size + sizeof(double));
            try
            {
                var bMemory = b.AsMemory(0, size);

                await file.ReadAsync(bMemory, token);

                var i = offset + bMemory.Span.IndexOf(特征.Span) + 特征.Length;

                var outBytes = b.AsMemory(size, sizeof(double));
                BitConverter.TryWriteBytes(outBytes.Span, duration);

                if (BitConverter.IsLittleEndian)
                {
                    outBytes.Span.Reverse();
                }

                file.Position = i;
                await file.WriteAsync(outBytes, token);
            }
            finally
            {
                ArrayPool.Return(b);
            }
        }

        private async ValueTask CopyFixedSizeAsync(Stream source, Stream dst, int size, CancellationToken token)
        {
            var buffer = ArrayPool.Rent(Math.Min(BufferSize, size));
            try
            {
                var memory = buffer.AsMemory();
                while (true)
                {
                    var shouldRead = Math.Min(size, memory.Length);

                    var read = await source.ReadAsync(memory.Slice(0, shouldRead), token);
                    if (read <= 0)
                    {
                        break;
                    }

                    size -= read;
                    await dst.WriteAsync(memory.Slice(0, read), token);
                }
            }
            finally
            {
                ArrayPool.Return(buffer);
            }
        }

        private static async ValueTask<int> ReadInt32BigEndianAsync(Stream fs, int offset, CancellationToken token)
        {
            var origin = fs.Position;
            fs.Position = offset;

            var buffer = ArrayPool.Rent(4);
            try
            {
                var memory = buffer.AsMemory(0, 4);
                await fs.ReadAsync(memory, token);

                fs.Position = origin;

                return BinaryPrimitives.ReadInt32BigEndian(memory.Span);
            }
            finally
            {
                ArrayPool.Return(buffer);
            }
        }

        private static async ValueTask<uint> GetTimeStampAsync(Stream fs, int offset, CancellationToken token)
        {
            var origin = fs.Position;
            fs.Position = offset;
            var buffer = ArrayPool.Rent(4);
            try
            {
                var memory = buffer.AsMemory(0, 4);
                await fs.ReadAsync(memory, token);

                fs.Position = origin;

                var m = BinaryPrimitives.ReadUInt32BigEndian(memory.Span);

                return ((m & 0xFF) << 24) | (m >> 8);
            }
            finally
            {
                ArrayPool.Return(buffer);
            }
        }

        #endregion
    }
}
