using BilibiliLiveRecordDownLoader.FlvProcessor.Enums;
using BilibiliLiveRecordDownLoader.FlvProcessor.Models;
using BilibiliLiveRecordDownLoader.FlvProcessor.Models.FlvTagHeaders;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Buffers;

namespace UnitTest
{
    [TestClass]
    public class FlvTest
    {
        [TestMethod]
        public void TestFlvHeader()
        {
            var should = new byte[] { 0x46, 0x4c, 0x56, 0x01, 0x05, 0x00, 0x00, 0x00, 0x09 };

            IBytesStruct header = new FlvHeader();

            var b = ArrayPool<byte>.Shared.Rent(header.Size);
            try
            {
                Assert.IsTrue(header.ToMemory(b).Span.SequenceEqual(should));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(b);
            }
        }

        [TestMethod]
        public void TestFlvTagPayloadInfo()
        {
            var should = new byte[] { 0x09, 0xFF, 0xFF, 0xFF };

            var info = new FlvTagPayloadInfo
            {
                PayloadSize = (1 << 24) - 1,
                PacketType = PacketType.VideoPayload
            };

            var b = ArrayPool<byte>.Shared.Rent(info.Size);
            try
            {
                Assert.IsTrue(info.ToMemory(b).Span.SequenceEqual(should));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(b);
            }
        }

        [TestMethod]
        public void TestStreamId()
        {
            var should = new byte[] { 0x00, 0x00, 0x00 };

            IBytesStruct info = new StreamId();

            var b = ArrayPool<byte>.Shared.Rent(info.Size);
            try
            {
                Assert.IsTrue(info.ToMemory(b).Span.SequenceEqual(should));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(b);
            }
        }

        [TestMethod]
        public void TestTimestamp()
        {
            var should = new byte[] { 0xC7, 0x00, 0x42, 0x9F };

            var info = new Timestamp
            {
                Lower = 0x12C7_0042,
                Upper = 0x9F
            };

            var b = ArrayPool<byte>.Shared.Rent(info.Size);
            try
            {
                Assert.IsTrue(info.ToMemory(b).Span.SequenceEqual(should));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(b);
            }
        }

        [TestMethod]
        public void TestFlvTagHeader()
        {
            var should = new byte[]
            {
                0x00, 0x01, 0xBF, 0x52,
                0x09,
                0xFF, 0xFF, 0xFF,
                0xC7, 0x00, 0x42,
                0x9F,
                0x00, 0x00, 0x00
            };

            var info = new FlvTagHeader
            {
                SizeofPreviousPacket = 114514,
                PayloadInfo = { PayloadSize = (1 << 24) - 1, PacketType = PacketType.VideoPayload },
                Timestamp = { Lower = 0x12C7_0042, Upper = 0x9F }
            };

            var b = ArrayPool<byte>.Shared.Rent(info.Size);
            try
            {
                Assert.IsTrue(info.ToMemory(b).Span.SequenceEqual(should));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(b);
            }
        }
    }
}
