using BilibiliLive.Danmu;
using System.IO.Compression;
using System.Text;

namespace BilibiliLive.Tests.Danmu;

public class DanmuPacketTest
{
	internal static byte[] Compress(byte[] data, Func<Stream, Stream> createCompressor)
	{
		using MemoryStream output = new();

		using (Stream compressor = createCompressor(output))
		{
			compressor.Write(data);
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
		return DanmuPacket.Encode(DanmuOperation.Message, 2, Compress([.. packets.SelectMany(p => p)], static s => new ZLibStream(s, CompressionLevel.Optimal)));
	}

	internal static byte[] Brotli(params byte[][] packets)
	{
		return DanmuPacket.Encode(DanmuOperation.Message, 3, Compress([.. packets.SelectMany(p => p)], static s => new BrotliStream(s, CompressionLevel.Optimal)));
	}

	internal static byte[] Json(string json, ushort version = 0)
	{
		return DanmuPacket.Encode(DanmuOperation.Message, version, Encoding.UTF8.GetBytes(json));
	}

	private static string[] Bodies(IEnumerable<DanmuPacket> packets)
	{
		return [.. packets.Select(p => Encoding.UTF8.GetString(p.Body.Span))];
	}

	[Test]
	public async Task EncodeWritesBigEndianHeaderAsync()
	{
		byte[] packet = DanmuPacket.Encode(DanmuOperation.Auth, 1, "{}"u8);

		await Sequences.AssertEqualAsync<byte>(packet, [0, 0, 0, 18, 0, 16, 0, 1, 0, 0, 0, 7, 0, 0, 0, 1, (byte)'{', (byte)'}']);
	}

	[Test]
	public async Task DecodeSplitsConcatenatedPacketsAsync()
	{
		byte[] data = [.. Json("""{"cmd":"A"}"""), .. DanmuPacket.Encode(DanmuOperation.HeartbeatReply, 1, [0, 0, 0, 42]), .. Json("""{"cmd":"B"}""", 1)];

		List<DanmuPacket> packets = DanmuPacket.Decode(data);

		using (Assert.Multiple())
		{
			await Sequences.AssertEqualAsync(packets.Select(p => (p.ProtocolVersion, p.Operation)), [((ushort)0, DanmuOperation.Message), ((ushort)1, DanmuOperation.HeartbeatReply), ((ushort)1, DanmuOperation.Message)]);
			await Sequences.AssertEqualAsync<byte>(packets[1].Body.ToArray(), [0, 0, 0, 42]);
			await Assert.That(Encoding.UTF8.GetString(packets[2].Body.Span)).IsEqualTo("""{"cmd":"B"}""");
		}
	}

	[Test]
	public async Task DecodeExpandsNestedBrotliAndZlibPacketsAsync()
	{
		byte[] data = [.. Json("""{"cmd":"before"}"""), .. Brotli(Json("""{"cmd":"A"}"""), Zlib(Json("""{"cmd":"B"}"""), Json("""{"cmd":"C"}""")), Json("""{"cmd":"D"}""")), .. Json("""{"cmd":"after"}""")];

		await Sequences.AssertEqualAsync(Bodies(DanmuPacket.Decode(data)), ["""{"cmd":"before"}""", """{"cmd":"A"}""", """{"cmd":"B"}""", """{"cmd":"C"}""", """{"cmd":"D"}""", """{"cmd":"after"}"""]);
	}

	[Test]
	public async Task DecodeRespectsLongerHeaderLengthAsync()
	{
		byte[] packet = [0, 0, 0, 22, 0, 20, 0, 0, 0, 0, 0, 5, 0, 0, 0, 1, 9, 9, 9, 9, (byte)'o', (byte)'k'];

		await Sequences.AssertEqualAsync(Bodies(DanmuPacket.Decode(packet)), ["ok"]);
	}

	[Test]
	public async Task DecodeRejectsTruncatedHeaderAsync()
	{
		byte[] data = [.. Json("{}"), 0, 0, 0];

		await Assert.That(() => DanmuPacket.Decode(data)).Throws<InvalidDataException>();
	}

	[Test]
	public async Task DecodeRejectsLengthBeyondBufferAsync()
	{
		byte[] data = Json("{}");
		data[3] += 1;

		await Assert.That(() => DanmuPacket.Decode(data)).Throws<InvalidDataException>();
	}

	[Test]
	public async Task DecodeRejectsHeaderShorterThanSixteenBytesAsync()
	{
		byte[] data = Json("{}");
		data[5] = 8;

		await Assert.That(() => DanmuPacket.Decode(data)).Throws<InvalidDataException>();
	}

	[Test]
	public async Task DecodeRejectsPacketLongerThanTheLimitAsync()
	{
		byte[] data = DanmuPacket.Encode(DanmuOperation.Message, 0, new byte[DanmuPacket.MaxPacketLength - DanmuPacket.HeaderLength + 1]);

		await Assert.That(() => DanmuPacket.Decode(data)).Throws<InvalidDataException>();
	}

	[Test]
	public async Task DecodeRejectsCompressionBombAsync()
	{
		// Each packet and their sum stay within the size limits, so only the ratio can reject it
		byte[] zeros = DanmuPacket.Encode(DanmuOperation.Message, 0, new byte[DanmuPacket.MaxPacketLength / 2]);
		byte[] data = Zlib(zeros, zeros, zeros);

		await Assert.That(() => DanmuPacket.Decode(data)).Throws<InvalidDataException>();
	}

	[Test]
	public async Task DecodeRejectsDecompressedPacketsBeyondTheSizeLimitAsync()
	{
		// Random bytes do not compress, so every packet stays within the ratio and only their sum exceeds the limit
		Random random = new(42);
		byte[] body = new byte[DanmuPacket.MaxPacketLength / 2];
		byte[] data = [.. Enumerable.Range(0, DanmuPacket.MaxDecompressedLength / body.Length + 1).SelectMany(_ =>
		{
			random.NextBytes(body);
			return Zlib(DanmuPacket.Encode(DanmuOperation.Message, 0, body));
		})];

		await Assert.That(() => DanmuPacket.Decode(data)).Throws<InvalidDataException>();
	}

	[Test]
	public async Task DecodeLimitsCompressionDepthAsync()
	{
		byte[] packet = Json("""{"cmd":"A"}""");

		using (Assert.Multiple())
		{
			await Sequences.AssertEqualAsync(Bodies(DanmuPacket.Decode(Nest(packet, DanmuPacket.MaxCompressionDepth))), ["""{"cmd":"A"}"""]);
			await Assert.That(() => DanmuPacket.Decode(Nest(packet, DanmuPacket.MaxCompressionDepth + 1))).Throws<InvalidDataException>();
		}
	}
}
