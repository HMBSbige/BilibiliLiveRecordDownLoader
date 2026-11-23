using BilibiliLive.Danmu;
using System.Buffers;
using System.IO.Compression;
using System.Text;

namespace BilibiliLive.Tests.Danmu;

public class DanmuPacketTest
{
	private static byte[] Compress(byte[][] packets, Func<Stream, Stream> createCompressor)
	{
		using MemoryStream output = new();

		using (Stream compressor = createCompressor(output))
		{
			foreach (byte[] packet in packets)
			{
				compressor.Write(packet);
			}
		}

		return output.ToArray();
	}

	private static byte[] Nest(byte[] packet, int depth)
	{
		for (int i = 0; i < depth; ++i)
		{
			packet = Zlib(packet);
		}

		return packet;
	}

	internal static byte[] Zlib(params byte[][] packets)
	{
		return DanmuPacket.Encode(DanmuOperation.Message, 2, Compress(packets, static s => new ZLibStream(s, CompressionLevel.Optimal)));
	}

	internal static byte[] Brotli(params byte[][] packets)
	{
		return DanmuPacket.Encode(DanmuOperation.Message, 3, Compress(packets, static s => new BrotliStream(s, CompressionLevel.Optimal)));
	}

	internal static byte[] Json(string json, ushort version = 0)
	{
		return DanmuPacket.Encode(DanmuOperation.Message, version, Encoding.UTF8.GetBytes(json));
	}

	private static IEnumerable<string> Bodies(IEnumerable<DanmuPacket> packets)
	{
		return packets.Select(static p => Encoding.UTF8.GetString(p.Body.Span));
	}

	public static IEnumerable<TestDataRow<Func<byte[]>>> MalformedData()
	{
		yield return new(static () => [.. Json("{}"), 0, 0, 0], DisplayName: "Decode rejects a truncated header");
		yield return new(static () =>
		{
			byte[] data = Json("{}");
			++data[3];
			return data;
		}, DisplayName: "Decode rejects a length beyond the buffer");
		yield return new(static () =>
		{
			byte[] data = Json("{}");
			data[5] = 8;
			return data;
		}, DisplayName: "Decode rejects a header shorter than sixteen bytes");
		yield return new(static () => DanmuPacket.Encode(DanmuOperation.Message, 0, new byte[DanmuPacket.MaxPacketLength - DanmuPacket.HeaderLength + 1]), DisplayName: "Decode rejects a packet longer than the limit");
		// 每个包和它们的总和都没超出大小限制，只有压缩比能拒绝它
		yield return new(static () =>
		{
			byte[] zeros = DanmuPacket.Encode(DanmuOperation.Message, 0, new byte[DanmuPacket.MaxPacketLength / 2]);
			return Zlib(zeros, zeros, zeros);
		}, DisplayName: "Decode rejects a compression bomb");
		// 随机字节压缩不了，每个包都没超出压缩比，只有总和超出大小限制
		yield return new(static () =>
		{
			Random random = new(42);
			byte[] body = new byte[DanmuPacket.MaxPacketLength / 2];
			ArrayBufferWriter<byte> data = new();

			for (int i = 0; i <= DanmuPacket.MaxDecompressedLength / body.Length; ++i)
			{
				random.NextBytes(body);
				data.Write(Zlib(DanmuPacket.Encode(DanmuOperation.Message, 0, body)));
			}

			return data.WrittenSpan.ToArray();
		}, DisplayName: "Decode rejects decompressed packets beyond the size limit");
	}

	[Test]
	public async Task EncodeWritesBigEndianHeaderAsync()
	{
		byte[] packet = DanmuPacket.Encode(DanmuOperation.Auth, 1, "{}"u8);

		await Assert.That(packet).IsEquivalentTo((byte[])[0, 0, 0, 18, 0, 16, 0, 1, 0, 0, 0, 7, 0, 0, 0, 1, (byte)'{', (byte)'}'], CollectionOrdering.Matching);
	}

	[Test]
	public async Task DecodeSplitsConcatenatedPacketsAsync()
	{
		byte[] data = [.. Json("""{"cmd":"A"}"""), .. DanmuPacket.Encode(DanmuOperation.HeartbeatReply, 1, [0, 0, 0, 42]), .. Json("""{"cmd":"B"}""", 1)];

		List<DanmuPacket> packets = DanmuPacket.Decode(data);

		await Assert.That(packets.Select(static p => (p.ProtocolVersion, p.Operation))).IsEquivalentTo([((ushort)0, DanmuOperation.Message), ((ushort)1, DanmuOperation.HeartbeatReply), ((ushort)1, DanmuOperation.Message)], CollectionOrdering.Matching);

		using (Assert.Multiple())
		{
			await Assert.That(packets[1].Body.ToArray()).IsEquivalentTo((byte[])[0, 0, 0, 42], CollectionOrdering.Matching);
			await Assert.That(Encoding.UTF8.GetString(packets[2].Body.Span)).IsEqualTo("""{"cmd":"B"}""");
		}
	}

	[Test]
	public async Task DecodeExpandsNestedBrotliAndZlibPacketsAsync()
	{
		byte[] data = [.. Json("""{"cmd":"before"}"""), .. Brotli(Json("""{"cmd":"A"}"""), Zlib(Json("""{"cmd":"B"}"""), Json("""{"cmd":"C"}""")), Json("""{"cmd":"D"}""")), .. Json("""{"cmd":"after"}""")];

		await Assert.That(Bodies(DanmuPacket.Decode(data))).IsEquivalentTo(["""{"cmd":"before"}""", """{"cmd":"A"}""", """{"cmd":"B"}""", """{"cmd":"C"}""", """{"cmd":"D"}""", """{"cmd":"after"}"""], CollectionOrdering.Matching);
	}

	[Test]
	public async Task DecodeRespectsLongerHeaderLengthAsync()
	{
		byte[] packet = [0, 0, 0, 22, 0, 20, 0, 0, 0, 0, 0, 5, 0, 0, 0, 1, 9, 9, 9, 9, (byte)'o', (byte)'k'];

		await Assert.That(Bodies(DanmuPacket.Decode(packet))).IsEquivalentTo(["ok"], CollectionOrdering.Matching);
	}

	[Test]
	[MethodDataSource(nameof(MalformedData))]
	public async Task DecodeRejectsMalformedDataAsync(byte[] data)
	{
		await Assert.That(() => DanmuPacket.Decode(data)).Throws<InvalidDataException>();
	}

	[Test]
	public async Task DecodeLimitsCompressionDepthAsync()
	{
		byte[] packet = Json("""{"cmd":"A"}""");

		using (Assert.Multiple())
		{
			await Assert.That(Bodies(DanmuPacket.Decode(Nest(packet, DanmuPacket.MaxCompressionDepth)))).IsEquivalentTo(["""{"cmd":"A"}"""], CollectionOrdering.Matching);
			await Assert.That(() => DanmuPacket.Decode(Nest(packet, DanmuPacket.MaxCompressionDepth + 1))).Throws<InvalidDataException>();
		}
	}
}
