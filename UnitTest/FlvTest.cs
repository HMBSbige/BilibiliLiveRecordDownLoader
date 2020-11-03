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

            var header = new FlvHeader();

            var b = ArrayPool<byte>.Shared.Rent(header.Size);
            try
            {
                Assert.IsTrue(header.ToMemory(b).Span.SequenceEqual(should));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(b);
            }

            header.Signature = string.Empty;
            header.Version = 0x00;
            header.Flags = HeaderFlags.Audio;
            header.HeaderSize = 114514;

            header.Read(should);
            Assert.AreEqual(header.Signature, @"FLV");
            Assert.AreEqual(header.Version, 0x01);
            Assert.AreEqual(header.Flags, HeaderFlags.VideoAndAudio);
            Assert.AreEqual(header.HeaderSize, 9u);
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

            info.PayloadSize = 114514;
            info.PacketType = PacketType.AMF_Metadata;
            info.Read(should);
            Assert.AreEqual(info.PayloadSize, (uint)((1 << 24) - 1));
            Assert.AreEqual(info.PacketType, PacketType.VideoPayload);
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

            var info = new FlvTimestamp
            {
                Data = 0x9FC7_0042
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

            info.Data = 114514;
            info.Read(should);
            Assert.AreEqual(info.Data, 0x9FC7_0042);
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
                Timestamp = { Data = 0x9FC7_0042 }
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

            info.SizeofPreviousPacket = 1919810;
            info.PayloadInfo = new FlvTagPayloadInfo();
            info.Timestamp = new FlvTimestamp();
            info.Read(should);
            Assert.AreEqual(info.SizeofPreviousPacket, 114514u);
            Assert.AreEqual(info.PayloadInfo.PayloadSize, (uint)((1 << 24) - 1));
            Assert.AreEqual(info.PayloadInfo.PacketType, PacketType.VideoPayload);
            Assert.AreEqual(info.Timestamp.Data, 0x9FC7_0042);
        }
    }
}
