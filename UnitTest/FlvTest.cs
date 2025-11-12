using BilibiliLiveRecordDownLoader.FlvProcessor.Enums;
using BilibiliLiveRecordDownLoader.FlvProcessor.Interfaces;
using BilibiliLiveRecordDownLoader.FlvProcessor.Models;
using BilibiliLiveRecordDownLoader.FlvProcessor.Models.FlvTagHeaders;
using BilibiliLiveRecordDownLoader.FlvProcessor.Models.FlvTagPackets;
using BilibiliLiveRecordDownLoader.FlvProcessor.Utils;
using System.Buffers;

namespace UnitTest;

[TestClass]
public class FlvTest
{
	[TestMethod]
	public void TestFlvHeader()
	{
		var should = new byte[] { 0x46, 0x4c, 0x56, 0x01, 0x05, 0x00, 0x00, 0x00, 0x09, 0x00, 0x00, 0x00, 0x00 };

		var header = new FlvHeader();

		using (var memory = MemoryPool<byte>.Shared.Rent(header.Size))
		{
			Assert.IsTrue(header.ToMemory(memory.Memory).Span.SequenceEqual(should));
		}

		header.Signature = string.Empty;
		header.Version = 0x00;
		header.Flags = HeaderFlags.Audio;
		header.HeaderSize = 114514;

		header.Read(should);
		Assert.AreEqual(@"FLV", header.Signature);
		Assert.AreEqual(0x01, header.Version);
		Assert.AreEqual(HeaderFlags.VideoAndAudio, header.Flags);
		Assert.AreEqual(9u, header.HeaderSize);
		Assert.AreEqual(0u, header.Reserved);
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

		using (var memory = MemoryPool<byte>.Shared.Rent(info.Size))
		{
			Assert.IsTrue(info.ToMemory(memory.Memory).Span.SequenceEqual(should));
		}

		info.PayloadSize = 114514;
		info.PacketType = PacketType.AMF_Metadata;
		info.Read(should);
		Assert.AreEqual((uint)((1 << 24) - 1), info.PayloadSize);
		Assert.AreEqual(PacketType.VideoPayload, info.PacketType);
	}

	[TestMethod]
	public void TestStreamId()
	{
		var should = new byte[] { 0x00, 0x00, 0x00 };

		IBytesStruct info = new StreamId();

		using var memory = MemoryPool<byte>.Shared.Rent(info.Size);
		Assert.IsTrue(info.ToMemory(memory.Memory).Span.SequenceEqual(should));
	}

	[TestMethod]
	public void TestTimestamp()
	{
		var should = new byte[] { 0xC7, 0x00, 0x42, 0x9F };

		var info = new FlvTimestamp
		{
			Data = 0x9FC7_0042
		};

		using (var memory = MemoryPool<byte>.Shared.Rent(info.Size))
		{
			Assert.IsTrue(info.ToMemory(memory.Memory).Span.SequenceEqual(should));
		}

		info.Data = 114514;
		info.Read(should);
		Assert.AreEqual(0x9FC7_0042, info.Data);
	}

	[TestMethod]
	public void TestFlvTagHeader()
	{
		var should = new byte[]
		{
			0x09,
			0xFF, 0xFF, 0xFF,
			0xC7, 0x00, 0x42,
			0x9F,
			0x00, 0x00, 0x00
		};

		var info = new FlvTagHeader
		{
			PayloadInfo = { PayloadSize = (1 << 24) - 1, PacketType = PacketType.VideoPayload },
			Timestamp = { Data = 0x9FC7_0042 }
		};

		using (var memory = MemoryPool<byte>.Shared.Rent(info.Size))
		{
			Assert.IsTrue(info.ToMemory(memory.Memory).Span.SequenceEqual(should));
		}

		info.PayloadInfo = new();
		info.Timestamp = new();
		info.Read(should);
		Assert.AreEqual((uint)((1 << 24) - 1), info.PayloadInfo.PayloadSize);
		Assert.AreEqual(PacketType.VideoPayload, info.PayloadInfo.PacketType);
		Assert.AreEqual(0x9FC7_0042, info.Timestamp.Data);
	}

	[TestMethod]
	public void TestMetadata()
	{
		var should = new byte[]
		{
			0x02, 0x00, 0x0a, 0x6f, 0x6e, 0x4d, 0x65, 0x74,
			0x61, 0x44, 0x61, 0x74, 0x61, 0x08, 0x00, 0x00,
			0x00, 0x07, 0x00, 0x08, 0x64, 0x75, 0x72, 0x61,
			0x74, 0x69, 0x6f, 0x6e, 0x00, 0x40, 0x10, 0x00,
			0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x05, 0x77,
			0x69, 0x64, 0x74, 0x68, 0x00, 0x40, 0x66, 0x80,
			0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x06, 0x68,
			0x65, 0x69, 0x67, 0x68, 0x74, 0x00, 0x40, 0x66,
			0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0d,
			0x76, 0x69, 0x64, 0x65, 0x6f, 0x64, 0x61, 0x74,
			0x61, 0x72, 0x61, 0x74, 0x65, 0x00, 0x40, 0x3a,
			0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0d,
			0x61, 0x75, 0x64, 0x69, 0x6f, 0x64, 0x61, 0x74,
			0x61, 0x72, 0x61, 0x74, 0x65, 0x00, 0x40, 0x30,
			0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x09,
			0x66, 0x72, 0x61, 0x6d, 0x65, 0x72, 0x61, 0x74,
			0x65, 0x00, 0x40, 0x39, 0x00, 0x00, 0x00, 0x00,
			0x00, 0x00, 0x00, 0x0c, 0x63, 0x72, 0x65, 0x61,
			0x74, 0x69, 0x6f, 0x6e, 0x64, 0x61, 0x74, 0x65,
			0x02, 0x00, 0x19, 0x53, 0x75, 0x6e, 0x20, 0x4a,
			0x75, 0x6c, 0x20, 0x30, 0x33, 0x20, 0x32, 0x30,
			0x3a, 0x30, 0x39, 0x3a, 0x31, 0x37, 0x20, 0x32,
			0x30, 0x30, 0x35, 0x0a, 0x00, 0x00, 0x09
		};

		var metaData = new AMFMetadata
		{
			UseArray = true,
			Data =
			{
				{@"width",180.0},
				{@"height",180.0},
				{@"videodatarate",26.0},
				{@"audiodatarate",16.0},
				{@"framerate",25.0},
				{@"creationdate","Sun Jul 03 20:09:17 2005\n"}
			}
		};
		metaData.Data[@"duration"] = 4.0;

		Assert.AreEqual(183, metaData.Size);

		using (var memory = MemoryPool<byte>.Shared.Rent(metaData.Size))
		{
			Assert.IsTrue(metaData.ToMemory(memory.Memory).Span.SequenceEqual(should));
		}

		metaData.Data.Clear();
		metaData.Read(should);

		Assert.AreEqual(183, metaData.Size);
		Assert.HasCount(7, metaData.Data);
		Assert.AreEqual(4.0, metaData.Data[@"duration"]);
		Assert.AreEqual(180.0, metaData.Data[@"width"]);
		Assert.AreEqual(180.0, metaData.Data[@"height"]);
		Assert.AreEqual(26.0, metaData.Data[@"videodatarate"]);
		Assert.AreEqual(16.0, metaData.Data[@"audiodatarate"]);
		Assert.AreEqual(25.0, metaData.Data[@"framerate"]);
		Assert.AreEqual("Sun Jul 03 20:09:17 2005\n", metaData.Data[@"creationdate"]);
	}

	[TestMethod]
	public void TestFlvHeaderFlag()
	{
		Assert.AreEqual(HeaderFlags.Video, ((byte)0b0000_0001).ToFlvHeaderFlags());
		Assert.AreEqual(HeaderFlags.Audio, ((byte)0b0000_0100).ToFlvHeaderFlags());
		Assert.AreEqual(HeaderFlags.VideoAndAudio, ((byte)0b0000_0101).ToFlvHeaderFlags());

		Assert.AreEqual(HeaderFlags.Video, ((byte)0b1111_1011).ToFlvHeaderFlags());
		Assert.AreEqual(HeaderFlags.Audio, ((byte)0b1111_1110).ToFlvHeaderFlags());
		Assert.AreEqual(HeaderFlags.VideoAndAudio, ((byte)0b1111_1111).ToFlvHeaderFlags());
		Assert.AreNotEqual(HeaderFlags.Video, ((byte)0b0000_0000).ToFlvHeaderFlags());
		Assert.AreNotEqual(HeaderFlags.Audio, ((byte)0b0000_0000).ToFlvHeaderFlags());
		Assert.AreNotEqual(HeaderFlags.VideoAndAudio, ((byte)0b0000_0000).ToFlvHeaderFlags());
	}
}
