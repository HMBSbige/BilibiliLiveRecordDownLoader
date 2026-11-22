using BilibiliLiveRecordDownLoader.Application.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

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
	[Arguments(false)]
	[Arguments(true)]
	public async Task Mp4SizeComesFromTheMovieBoxWhereverItIsAsync(bool isMovieLast, CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine("a.mp4");
		byte[] init = Fmp4Samples.TimedInit(1000, 1080, 1920);
		byte[] media = Fmp4Samples.Box("mdat", new byte[4096]);

		// 没做 faststart 的文件把 moov 放在 mdat 之后
		int ftypLength = 8 + 4;
		byte[] content = isMovieLast ? [.. init.AsSpan(0, ftypLength), .. media, .. init.AsSpan(ftypLength)] : [.. init, .. media];
		await File.WriteAllBytesAsync(path, content, cancellationToken);

		await Assert.That(await VideoResolutionProbe.ReadAsync(path, cancellationToken)).IsEqualTo(new VideoResolution(1080, 1920));
	}

	[Test]
	public async Task UnknownContainerHasNoSizeAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine("a.mkv");
		await File.WriteAllBytesAsync(path, new byte[64], cancellationToken);

		await Assert.That(await VideoResolutionProbe.ReadAsync(path, cancellationToken)).IsNull();
	}
}
