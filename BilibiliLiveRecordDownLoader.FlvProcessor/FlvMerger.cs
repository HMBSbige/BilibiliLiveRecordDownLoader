using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BilibiliLiveRecordDownLoader.FlvProcessor
{
    public class FlvMerger
    {
        private const int TagHeaderSize = 4 + 1 + 3 + 3 + 1 + 3;

        public int BufferSize { get; set; } = 4 * 1024;

        public List<string> Files { get; }

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

        public void Merge(string path)
        {
            using var outFile = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

            // 读 Header
            using var f0 = File.OpenRead(Files.First());
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

                using var fs = File.OpenRead(fileName);

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

                        Span<byte> buffer = new byte[8 + 4];
                        fs.Position = i;
                        fs.Read(buffer);

                        if (fileNum > 0) //不是第一个文件的话，重写时间戳
                        {
                            var b = GetTimeStamp(currentTimestamp + timestamp);
                            b.CopyTo(buffer.Slice(8));
                        }

                        outFile.Write(buffer);

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
            Span<byte> b = stackalloc byte[size];
            file.Read(b);

            Span<byte> d = stackalloc byte[] { 0x08, 0x64, 0x75, 0x72, 0x61, 0x74, 0x69, 0x6F, 0x6E, 0x00 };

            var i = offset + b.IndexOf(d) + d.Length;

            Span<byte> outBytes = stackalloc byte[sizeof(double)];
            BitConverter.TryWriteBytes(outBytes, duration);
            if (BitConverter.IsLittleEndian)
            {
                outBytes.Reverse();
            }

            file.Position = i;
            file.Write(outBytes);
        }

        private void CopyFixedSize(Stream source, Stream dst, int size)
        {
            Span<byte> buffer = stackalloc byte[BufferSize];
            while (true)
            {
                var shouldRead = Math.Min(size, BufferSize);

                var read = source.Read(buffer.Slice(0, shouldRead));
                if (read <= 0)
                {
                    break;
                }
                size -= read;
                dst.Write(buffer.Slice(0, read));
            }
        }

        private static int ReadInt32BigEndian(Stream fs, int offset)
        {
            var origin = fs.Position;

            fs.Position = offset;
            Span<byte> buffer = stackalloc byte[4];
            fs.Read(buffer);

            fs.Position = origin;

            return BinaryPrimitives.ReadInt32BigEndian(buffer);
        }

        private static uint GetTimeStamp(Stream fs, int offset)
        {
            var origin = fs.Position;

            fs.Position = offset;
            Span<byte> buffer = stackalloc byte[4];
            fs.Read(buffer);

            fs.Position = origin;

            var m = BinaryPrimitives.ReadUInt32BigEndian(buffer);

            return ((m & 0xFF) << 24) | (m >> 8);
        }

        private static Span<byte> GetTimeStamp(uint timeStamp)
        {
            Span<byte> b = stackalloc byte[4];
            BitConverter.TryWriteBytes(b, timeStamp);
            return BitConverter.IsLittleEndian ? new[] { b[2], b[1], b[0], b[3] } : new[] { b[1], b[2], b[3], b[0] };
        }
    }
}
