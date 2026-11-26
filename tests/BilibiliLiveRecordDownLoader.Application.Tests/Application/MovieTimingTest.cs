using BilibiliLiveRecordDownLoader.Application.Recording.Hybrid;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class MovieTimingTest
{
	// 各轨道时间刻度的最小公倍数；比毫秒粗时取不小于 1000 的倍数，太大时退回毫秒
	[Test]
	[Arguments(new uint[] { 90_000, 48_000 }, 720_000u)]
	[Arguments(new uint[] { 600 }, 1200u)]
	[Arguments(new uint[] { 25, 30 }, 1050u)]
	[Arguments(new uint[] { 2_147_483_629, 90_000 }, 1000u)]
	public async Task MovieTimescaleIsAMultipleOfEveryTrackTimescaleAsync(uint[] timescales, uint expected)
	{
		await Assert.That(MovieTiming.GetMovieTimescale(timescales)).IsEqualTo(expected);
	}

	// init 段声明了音频，分片里却没有音频样本：音频轨不写出，电影时间刻度也不算它
	[Test]
	public async Task MovieTimescaleIgnoresTracksWithoutSamplesAsync()
	{
		using TestDirectory directory = new();
		string path = directory.WriteAllBytes(HybridSamples.FileName, [.. HybridSamples.Init(), .. HybridSamples.Fragment(1, new RunSpec(HybridSamples.VideoTrack, 0, HybridSamples.Video(30)))]);

		HybridMp4Converter.Convert(path, []);

		await Assert.That(Mp4Inspector.ReadMovieHeader(await File.ReadAllBytesAsync(path)).Timescale).IsEqualTo(HybridSamples.VideoTimescale);
	}
}
