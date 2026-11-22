using BilibiliLiveRecordDownLoader.Application.Recording.Splitting;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class VideoCodecDetectionTest
{
	[Test]
	[Arguments("avc1", StreamCodec.Avc)]
	[Arguments("avc3", StreamCodec.Avc)]
	[Arguments("hvc1", StreamCodec.Hevc)]
	[Arguments("hev1", StreamCodec.Hevc)]
	[Arguments("av01", StreamCodec.Av1)]
	[Arguments("encv", null)]
	[Arguments("vp09", null)]
	public async Task CodecComesFromTheSampleEntryOfTheVideoTrackAsync(string sampleEntry, StreamCodec? expected)
	{
		await Assert.That(IsoBmff.ReadVideoCodec(FillerSamples.Init(FillerCodec.Avc, 4, sampleEntry))).IsEqualTo(expected);
	}

	// 音频轨的样本项是 mp4a，不能当成认不出的视频编码
	[Test]
	public async Task AudioTrackBeforeTheVideoTrackIsSkippedAsync()
	{
		await Assert.That(IsoBmff.ReadVideoCodec(FillerSamples.Init(FillerCodec.Hevc, 4, isAudioFirst: true))).IsEqualTo(StreamCodec.Hevc);
	}

	[Test]
	public async Task InitWithoutAVideoSampleEntryHasNoCodecAsync()
	{
		using (Assert.Multiple())
		{
			await Assert.That(IsoBmff.ReadVideoCodec([])).IsNull();
			await Assert.That(IsoBmff.ReadVideoCodec(Fmp4Samples.Fragment(true))).IsNull();
			await Assert.That(IsoBmff.ReadVideoCodec(Fmp4Samples.Init(hasVideo: false))).IsNull();
			// 视频轨里没有 stsd
			await Assert.That(IsoBmff.ReadVideoCodec(Fmp4Samples.Init())).IsNull();
		}
	}
}
