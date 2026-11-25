using BilibiliLiveRecordDownLoader.Application.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Tests.Application;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using System.Buffers.Binary;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Danmaku;

public sealed class VideoResolutionProbeTest
{
	[Test]
	public async Task FlvSizeComesFromTheMetadataAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine("a.flv");
		await File.WriteAllBytesAsync(path, [.. FlvSamples.Header, .. FlvSamples.MetadataWithSize(1280, 720), .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.KeyFrame, 0)], cancellationToken);

		await Assert.That(await VideoResolutionProbe.ReadAsync(path, cancellationToken)).IsEqualTo(new VideoResolution(1280, 720));
	}

	[Test]
	public async Task FlvWithoutMetadataTakesTheSizeFromTheSequenceHeaderAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine("a.flv");
		await File.WriteAllBytesAsync(path, [.. FlvSamples.Header, .. FlvSamples.AudioSequenceHeader(), .. FlvSamples.AvcSequenceHeader(Convert.FromHexString(MediaHeaderTest.Avc480))], cancellationToken);

		await Assert.That(await VideoResolutionProbe.ReadAsync(path, cancellationToken)).IsEqualTo(new VideoResolution(854, 480));
	}

	// 第一个视频 PES 不带 SPS 时接着往后找；HLS 分片开头的杂字节原样写进了录像，开头和中间都可能有
	[Test]
	[Arguments(false, 0, 0)]
	[Arguments(true, 0, 0)]
	[Arguments(false, 1, 0)]
	[Arguments(false, 0, 187)]
	public async Task TsSizeComesFromTheFirstSpsAsync(bool startsWithoutSps, int leadingJunk, int junkBeforeSps, CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine("a.ts");
		byte[] leading = startsWithoutSps ? TsSamples.VideoFrame(TsSamples.AvcSlice) : [];
		byte[] frame = TsSamples.TimedVideoFrame(TsSamples.AvcIdr, 0, true, Convert.FromHexString(MediaHeaderTest.Avc480));
		// 杂字节里的 0x47 不能当成包头
		byte[] content = [.. Enumerable.Repeat((byte)0x47, leadingJunk), .. TsSamples.Pat(), .. TsSamples.Pmt(), .. leading, .. new byte[junkBeforeSps], .. frame, .. TsSamples.Audio(), .. TsSamples.Audio()];
		await File.WriteAllBytesAsync(path, content, cancellationToken);

		await Assert.That(await VideoResolutionProbe.ReadAsync(path, cancellationToken)).IsEqualTo(new VideoResolution(854, 480));
	}

	// 转换后的 moov 在媒体数据之后，带着随时长增长的完整样本表；大表只占文件长度，不写内容
	[Test]
	public async Task Mp4SizeComesFromAMovieBoxAfterLargeMediaDataAndSampleTablesAsync(CancellationToken cancellationToken)
	{
		const uint tableLength = 128 << 20;
		using TestDirectory directory = new();
		string path = directory.Combine("a.mp4");
		byte[] entry = new byte[78];
		BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(24), 1080);
		BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(26), 1920);
		byte[] stbl = [.. Fmp4Samples.Box("stsd", [0, 0, 0, 0, 0, 0, 0, 1, .. Fmp4Samples.Box("avc1", entry)]), .. Fmp4Samples.Box("stsz", [])];
		byte[] video = Fmp4Samples.Box("trak", [.. Fmp4Samples.Tkhd(Fmp4Samples.VideoTrack), .. Fmp4Samples.Box("mdia", [.. Fmp4Samples.Hdlr("vide"), .. Fmp4Samples.Box("minf", Fmp4Samples.Box("stbl", stbl))])]);
		byte[] media = [0, 0, 0, 1, .. "mdat"u8, 0, 0, 0, 0, 0, 0, 0, 0, .. new byte[4096]];
		BinaryPrimitives.WriteUInt64BigEndian(media.AsSpan(8), (ulong)media.Length);
		byte[] content = [.. Fmp4Samples.Box("ftyp", "isom"u8.ToArray()), .. media, .. Fmp4Samples.Box("moov", [.. Fmp4Samples.Track(Fmp4Samples.AudioTrack, "soun"), .. video])];

		// 视频轨在最后，从 moov 到 stsz 每一层都以文件末尾结束
		foreach (string type in (string[])["moov", "trak", "mdia", "minf", "stbl", "stsz"])
		{
			Span<byte> size = content.AsSpan(content.AsSpan().LastIndexOf(Encoding.ASCII.GetBytes(type)) - 4, 4);
			BinaryPrimitives.WriteUInt32BigEndian(size, BinaryPrimitives.ReadUInt32BigEndian(size) + tableLength);
		}

		await using (FileStream file = File.Create(path))
		{
			await file.WriteAsync(content, cancellationToken);
			file.SetLength(content.Length + tableLength);
		}

		await Assert.That(await VideoResolutionProbe.ReadAsync(path, cancellationToken)).IsEqualTo(new VideoResolution(1080, 1920));
	}

	// 坏掉的 64 位盒子长度：一个加到偏移上会溢出成负数，一个转成有符号数就是负数
	[Test]
	[Arguments(0x7FFF_FFFF_FFFF_FFF0UL)]
	[Arguments(0x8000_0000_0000_0000UL)]
	public async Task Mp4WithAnImpossibleBoxSizeHasNoSizeAsync(ulong largeSize, CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine("a.mp4");
		byte[] header = [0, 0, 0, 1, .. "free"u8, 0, 0, 0, 0, 0, 0, 0, 0];
		BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(8), largeSize);
		await File.WriteAllBytesAsync(path, [.. Fmp4Samples.Box("ftyp", new byte[8]), .. header, .. Fmp4Samples.TimedInit(1000, 1080, 1920)], cancellationToken);

		await Assert.That(await VideoResolutionProbe.ReadAsync(path, cancellationToken)).IsNull();
	}
}
