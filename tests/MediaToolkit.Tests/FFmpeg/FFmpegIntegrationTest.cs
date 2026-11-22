using MediaToolkit.FFmpeg;
using MediaToolkit.Flv;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MediaToolkit.Tests.FFmpeg;

// 需要本机安装 FFmpeg，找不到时跳过
public partial class FFmpegIntegrationTest
{
	private const int TestTimeout = 120_000;

	private static readonly TimeSpan SampleDuration = TimeSpan.FromSeconds(2);
	// AAC 的编码延迟和 FLV 的起始时间戳会让容器时长比采样时长多出一点
	private static readonly TimeSpan Tolerance = TimeSpan.FromMilliseconds(300);

	[GeneratedRegex(@"frame=\s*(?<count>\d+)")]
	private static partial Regex FrameCountRegex();

	private static FFmpegRunner CreateRunner()
	{
		string path = FFmpegRunner.Locate(null) ?? string.Empty;
		Skip.When(path.Length is 0, @"FFmpeg is not installed.");
		return new FFmpegRunner(path);
	}

	private static async Task GenerateFlvAsync(FFmpegRunner runner, string output, CancellationToken cancellationToken)
	{
		string seconds = SampleDuration.TotalSeconds.ToString(CultureInfo.InvariantCulture);

		try
		{
			await runner.RunAsync(
			[
				@"-hide_banner", @"-y",
				@"-f", @"lavfi", @"-i", $@"testsrc=duration={seconds}:size=160x120:rate=10",
				@"-f", @"lavfi", @"-i", $@"sine=duration={seconds}",
				@"-c:v", @"libx264", @"-pix_fmt", @"yuv420p", @"-c:a", @"aac",
				@"-f", @"flv", output
			], null, cancellationToken);
		}
		catch (FFmpegException ex)
		{
			Skip.Test($@"FFmpeg cannot encode H.264/AAC FLV: {ex.Message}");
		}
	}

	// 解码到空输出，返回最后一次报告的进度和统计到的帧数
	private static async Task<(FFmpegProgress Progress, int Frames)> DecodeAsync(FFmpegRunner runner, string input, CancellationToken cancellationToken)
	{
		FFmpegProgress last = default;
		int frames = 0;

		await runner.RunAsync([@"-hide_banner", @"-i", input, @"-fps_mode", @"passthrough", @"-f", @"null", @"-"], new SyncProgress<FFmpegProgress>(progress =>
		{
			last = progress;

			if (FrameCountRegex().Match(progress.Message) is { Success: true } match)
			{
				frames = int.Parse(match.Groups[@"count"].ValueSpan, CultureInfo.InvariantCulture);
			}
		}), cancellationToken);

		return (last, frames);
	}

	[Test]
	[Timeout(TestTimeout)]
	public async Task GetVersionAsyncReturnsInstalledVersionAsync(CancellationToken cancellationToken)
	{
		FFmpegRunner runner = CreateRunner();

		string? version = await runner.GetVersionAsync(cancellationToken);

		await Assert.That(version).IsNotNull().And.IsNotEmpty();
	}

	[Test]
	[Timeout(TestTimeout)]
	public async Task ExtractedStreamsDecodeCompletelyAsync(CancellationToken cancellationToken)
	{
		FFmpegRunner runner = CreateRunner();
		using TestDirectory directory = new();
		string flv = directory.Combine(@"sample.flv");
		await GenerateFlvAsync(runner, flv, cancellationToken);

		FlvExtractResult result = await FlvExtractor.ExtractAsync(flv, directory.Root, null, cancellationToken);

		await Assert.That(result.VideoPath).IsNotNull();
		await Assert.That(result.AudioPath).IsNotNull();
		string video = result.VideoPath ?? string.Empty;
		string audio = result.AudioPath ?? string.Empty;

		byte[] videoBytes = await File.ReadAllBytesAsync(video, cancellationToken);
		await Assert.That(Convert.ToHexString(videoBytes, 0, 4)).IsEqualTo(@"00000001");
		// 第一个 NALU 应该是序列头里的 SPS
		await Assert.That(videoBytes[4] & 0x1F).IsEqualTo(7);

		// FFmpeg 的 ADTS 封装和这里写的头字段相同，可以逐字节对照
		string referenceAudio = directory.Combine(@"reference.aac");
		await runner.RunAsync([@"-hide_banner", @"-y", @"-i", flv, @"-map", @"0:a", @"-c", @"copy", @"-f", @"adts", referenceAudio], null, cancellationToken);
		string audioHex = Convert.ToHexString(await File.ReadAllBytesAsync(audio, cancellationToken));
		await Assert.That(audioHex).IsEqualTo(Convert.ToHexString(await File.ReadAllBytesAsync(referenceAudio, cancellationToken)));

		(_, int videoFrames) = await DecodeAsync(runner, video, cancellationToken);
		await Assert.That(videoFrames).IsEqualTo((int)(SampleDuration.TotalSeconds * 10));

		(FFmpegProgress audioProgress, _) = await DecodeAsync(runner, audio, cancellationToken);
		await Assert.That(audioProgress.Position).IsNotNull();
		await Assert.That(audioProgress.Position ?? TimeSpan.Zero).IsBetween(SampleDuration - Tolerance, SampleDuration + Tolerance);

		// 裸 H.264 没有时间戳，Matroska 拒绝写入，MP4 会按默认帧率补上
		string merged = directory.Combine(@"merged.mp4");
		await runner.RunAsync(FFmpegArguments.Merge(video, audio, merged), null, cancellationToken);
		(_, int mergedFrames) = await DecodeAsync(runner, merged, cancellationToken);
		// 用 FFmpeg 自己导出的 Annex-B 流合并也一样会少解出最后一帧
		await Assert.That(mergedFrames).IsGreaterThanOrEqualTo(videoFrames - 1);
	}

	[Test]
	[Timeout(TestTimeout)]
	public async Task RemuxKeepsDurationAndCutStopsAtEndAsync(CancellationToken cancellationToken)
	{
		FFmpegRunner runner = CreateRunner();
		using TestDirectory directory = new();
		string flv = directory.Combine(@"sample.flv");
		await GenerateFlvAsync(runner, flv, cancellationToken);

		string remuxed = directory.Combine(@"remuxed.mkv");
		List<FFmpegProgress> remuxProgress = [];
		await runner.RunAsync(FFmpegArguments.Remux(flv, remuxed), new SyncProgress<FFmpegProgress>(remuxProgress.Add), cancellationToken);

		await Assert.That(remuxProgress[^1].Duration ?? TimeSpan.Zero).IsBetween(SampleDuration - Tolerance, SampleDuration + Tolerance);

		string cut = directory.Combine(@"cut.mkv");
		await runner.RunAsync(FFmpegArguments.Cut(remuxed, cut, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1)), null, cancellationToken);
		(_, int cutFrames) = await DecodeAsync(runner, cut, cancellationToken);

		// 流复制的起点会退到之前的关键帧，只能确认 -to 生效：完整样本有 20 帧
		await Assert.That(cutFrames).IsBetween(1, 16);
	}

	[Test]
	[Timeout(TestTimeout)]
	public async Task RemuxWritesChaptersWithSpecialCharactersAsync(CancellationToken cancellationToken)
	{
		FFmpegRunner runner = CreateRunner();
		using TestDirectory directory = new();
		string flv = directory.Combine(@"sample.flv");
		await GenerateFlvAsync(runner, flv, cancellationToken);

		string chapters = directory.Combine(@"章节.txt");
		await FFmpegMetadata.WriteChaptersFileAsync(chapters,
		[
			new FFmpegChapter(TimeSpan.Zero, TimeSpan.FromSeconds(1), @"直播标题"),
			new FFmpegChapter(TimeSpan.FromSeconds(1), SampleDuration, @"高能：a=b;c#d\e")
		], cancellationToken);
		string mp4 = directory.Combine(@"remuxed.mp4");
		await runner.RunAsync(FFmpegArguments.RemuxWithChapters(flv, chapters, mp4), null, cancellationToken);

		// 把 MP4 里的章节再导出成 ffmetadata，导出时 FFmpeg 用同样的规则转义
		string exported = directory.Combine(@"exported.txt");
		await runner.RunAsync([@"-hide_banner", @"-y", @"-i", mp4, @"-f", @"ffmetadata", exported], null, cancellationToken);
		string metadata = await File.ReadAllTextAsync(exported, cancellationToken);
		(_, int frames) = await DecodeAsync(runner, mp4, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(metadata).Contains("title=直播标题\n");
			await Assert.That(metadata).Contains("title=高能：a\\=b\\;c\\#d\\\\e\n");
			await Assert.That(metadata.Split(@"[CHAPTER]").Length).IsEqualTo(3);
			await Assert.That(frames).IsEqualTo(20);
		}
	}

	[Test]
	[Timeout(TestTimeout)]
	public async Task RecordSendsHttpHeadersAndSavesStreamAsync(CancellationToken cancellationToken)
	{
		FFmpegRunner runner = CreateRunner();
		using TestDirectory directory = new();
		string flv = directory.Combine(@"sample.flv");
		await GenerateFlvAsync(runner, flv, cancellationToken);
		await using StaticHttpServer server = new(await File.ReadAllBytesAsync(flv, cancellationToken));
		string output = directory.Combine(@"record.ts");

		IReadOnlyList<string> arguments = FFmpegArguments.Record(server.GetUri(@"/live.flv?sign=a%2Fb"), @"MediaToolkit-Test/1.0", new Uri(@"https://live.bilibili.com/"), TimeSpan.FromSeconds(10));

		await using (FileStream file = File.Create(output))
		{
			await runner.RunAsync(arguments, stdout => stdout.CopyToAsync(file, cancellationToken), null, cancellationToken);
		}

		string request = server.RequestHeads.First();
		await Assert.That(request).StartsWith(@"GET /live.flv?sign=a%2Fb HTTP/1.1");
		await Assert.That(request).Contains("User-Agent: MediaToolkit-Test/1.0\r\n");
		await Assert.That(request).Contains("Referer: https://live.bilibili.com/\r\n");

		(_, int sourceFrames) = await DecodeAsync(runner, flv, cancellationToken);
		(FFmpegProgress recorded, int recordedFrames) = await DecodeAsync(runner, output, cancellationToken);
		await Assert.That(recordedFrames).IsEqualTo(sourceFrames);
		// 录制输出的是 MPEG-TS，容器时长的算法和 FLV 源不同，只能和采样时长比较
		await Assert.That(recorded.Duration ?? TimeSpan.Zero).IsBetween(SampleDuration - Tolerance, SampleDuration + Tolerance);
	}

	[Test]
	[Timeout(TestTimeout)]
	public async Task CancelledRecordingLeavesPlayableFileAsync(CancellationToken cancellationToken)
	{
		FFmpegRunner runner = CreateRunner();
		using TestDirectory directory = new();
		// MP4 的 moov 在收尾时才写入，被强杀的 MP4 无法打开
		string output = directory.Combine(@"live.mp4");
		using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		SyncProgress<FFmpegProgress> progress = new(p =>
		{
			if (p.Position >= TimeSpan.FromSeconds(1))
			{
				cts.Cancel();
			}
		});

		await Assert.That(async () => await runner.RunAsync([@"-hide_banner", @"-y", @"-re", @"-f", @"lavfi", @"-i", @"testsrc=size=160x120:rate=10", @"-c:v", @"mpeg4", output], progress, cts.Token)).Throws<OperationCanceledException>();

		(FFmpegProgress decoded, int frames) = await DecodeAsync(runner, output, cancellationToken);
		await Assert.That(frames).IsGreaterThanOrEqualTo(10);
		await Assert.That(decoded.Duration).IsNotNull();
	}
}
