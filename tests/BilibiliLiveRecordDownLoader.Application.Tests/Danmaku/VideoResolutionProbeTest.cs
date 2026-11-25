using BilibiliLiveRecordDownLoader.Application.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Tests.Application;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using System.Buffers.Binary;

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

	// 第一个视频 PES 不带 SPS 时接着往后找；失去包同步之后不再找
	[Test]
	[Arguments(false, false)]
	[Arguments(true, false)]
	[Arguments(false, true)]
	public async Task TsSizeComesFromTheFirstSpsInSyncAsync(bool startsWithoutSps, bool losesSync, CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine("a.ts");
		byte[] leading = startsWithoutSps ? TsSamples.VideoFrame(TsSamples.AvcSlice) : [];
		byte[] junk = losesSync ? [0] : [];
		await File.WriteAllBytesAsync(path, [.. TsSamples.Pat(), .. TsSamples.Pmt(), .. leading, .. junk, .. TsSamples.TimedVideoFrame(TsSamples.AvcIdr, 0, true, Convert.FromHexString(MediaHeaderTest.Avc480))], cancellationToken);

		await Assert.That(await VideoResolutionProbe.ReadAsync(path, cancellationToken)).IsEqualTo(losesSync ? null : new VideoResolution(854, 480));
	}

	[Test]
	[Arguments(true)]
	public async Task Mp4SizeComesFromTheMovieBoxWhereverItIsAsync(bool isMovieLast, CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine("a.mp4");
		byte[] init = Fmp4Samples.TimedInit(1000, 1080, 1920);
		byte[] media = Fmp4Samples.Box("mdat", new byte[4096]);

		const int ftypLength = 8 + 4;
		byte[] content = isMovieLast ? [.. init.AsSpan(0, ftypLength), .. media, .. init.AsSpan(ftypLength)] : [.. init, .. media];
		await File.WriteAllBytesAsync(path, content, cancellationToken);

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
