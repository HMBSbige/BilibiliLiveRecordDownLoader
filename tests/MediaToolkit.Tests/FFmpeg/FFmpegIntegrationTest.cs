using MediaToolkit.FFmpeg;
using MediaToolkit.Flv;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MediaToolkit.Tests.FFmpeg;

// 找不到 FFmpeg 时跳过；CI 设 REQUIRE_FFMPEG 改为失败，免得一直跳过没人发现
[Timeout(TestTimeout)]
public partial class FFmpegIntegrationTest
{
	private const int TestTimeout = 120_000;

	private static readonly bool IsFFmpegRequired = Environment.GetEnvironmentVariable(@"REQUIRE_FFMPEG") is { Length: > 0 };

	private static readonly TimeSpan SampleDuration = TimeSpan.FromSeconds(2);
	// AAC 的编码延迟和 FLV 的起始时间戳会让容器时长比采样时长多出一点
	private static readonly TimeSpan Tolerance = TimeSpan.FromMilliseconds(300);

	[GeneratedRegex(@"frame=\s*(?<count>\d+)")]
	private static partial Regex FrameCountRegex { get; }

	private static FFmpegRunner CreateRunner()
	{
		string path = FFmpegRunner.Locate(null) ?? string.Empty;
		SkipOrFail(path.Length is 0, @"FFmpeg is not installed.");
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
			SkipOrFail(true, $@"FFmpeg cannot encode H.264/AAC FLV: {ex.Message}");
		}
	}

	private static void SkipOrFail(bool condition, string reason)
	{
		if (condition && IsFFmpegRequired)
		{
			throw new InvalidOperationException(reason);
		}

		Skip.When(condition, reason);
	}

	private static async Task<(FFmpegProgress Progress, int Frames)> DecodeAsync(FFmpegRunner runner, string input, CancellationToken cancellationToken)
	{
		FFmpegProgress last = default;
		int frames = 0;

		await runner.RunAsync([@"-hide_banner", @"-i", input, @"-fps_mode", @"passthrough", @"-f", @"null", @"-"], new SyncProgress<FFmpegProgress>(progress =>
		{
			last = progress;

			if (FrameCountRegex.Match(progress.Message) is { Success: true } match)
			{
				frames = int.Parse(match.Groups[@"count"].ValueSpan, CultureInfo.InvariantCulture);
			}
		}), cancellationToken);

		return (last, frames);
	}

	[Test]
	public async Task GetVersionAsyncReturnsInstalledVersionAsync(CancellationToken cancellationToken)
	{
		FFmpegRunner runner = CreateRunner();

		string? version = await runner.GetVersionAsync(cancellationToken);

		await Assert.That(version).IsNotNull().And.IsNotEmpty();
	}

	[Test]
	public async Task ExtractedStreamsDecodeCompletelyAsync(CancellationToken cancellationToken)
	{
		FFmpegRunner runner = CreateRunner();
		using TestDirectory directory = new();
		string flv = directory.Combine(@"sample.flv");
		await GenerateFlvAsync(runner, flv, cancellationToken);

		FlvExtractResult result = await FlvExtractor.ExtractAsync(flv, directory.Root, null, cancellationToken);

		string video = await Assert.That(result.VideoPath).IsNotNull();
		string audio = await Assert.That(result.AudioPath).IsNotNull();

		byte[] videoBytes = await File.ReadAllBytesAsync(video, cancellationToken);
		await Assert.That(Convert.ToHexString(videoBytes, 0, 4)).IsEqualTo(@"00000001");
		// NAL 类型 7 是 SPS
		await Assert.That(videoBytes[4] & 0x1F).IsEqualTo(7);

		// FFmpeg 的 ADTS 封装和这里写的头字段相同，可以逐字节对照
		string referenceAudio = directory.Combine(@"reference.aac");
		await runner.RunAsync([@"-hide_banner", @"-y", @"-i", flv, @"-map", @"0:a", @"-c", @"copy", @"-f", @"adts", referenceAudio], null, cancellationToken);
		byte[] audioBytes = await File.ReadAllBytesAsync(audio, cancellationToken);
		byte[] referenceBytes = await File.ReadAllBytesAsync(referenceAudio, cancellationToken);
		await Assert.That((audioBytes.Length, audioBytes.AsSpan().CommonPrefixLength(referenceBytes))).IsEqualTo((referenceBytes.Length, referenceBytes.Length));

		(_, int videoFrames) = await DecodeAsync(runner, video, cancellationToken);
		await Assert.That(videoFrames).IsEqualTo((int)(SampleDuration.TotalSeconds * 10));

		(FFmpegProgress audioProgress, _) = await DecodeAsync(runner, audio, cancellationToken);
		TimeSpan position = await Assert.That(audioProgress.Position).IsNotNull();
		await Assert.That(position).IsBetween(SampleDuration - Tolerance, SampleDuration + Tolerance);

		// 裸 H.264 没有时间戳，Matroska 拒绝写入，MP4 会按默认帧率补上
		string merged = directory.Combine(@"merged.mp4");
		await runner.RunAsync(FFmpegArguments.Merge(video, audio, merged), null, cancellationToken);
		(_, int mergedFrames) = await DecodeAsync(runner, merged, cancellationToken);
		// 用 FFmpeg 自己导出的 Annex-B 流合并也一样会少解出最后一帧
		await Assert.That(mergedFrames).IsGreaterThanOrEqualTo(videoFrames - 1);
	}

	[Test]
	public async Task RemuxKeepsDurationAndCutStopsAtEndAsync(CancellationToken cancellationToken)
	{
		FFmpegRunner runner = CreateRunner();
		using TestDirectory directory = new();
		string flv = directory.Combine(@"sample.flv");
		await GenerateFlvAsync(runner, flv, cancellationToken);

		string remuxed = directory.Combine(@"remuxed.mkv");
		await runner.RunAsync(FFmpegArguments.Remux(flv, remuxed), null, cancellationToken);

		// 转封装时打印的 Duration 是输入的时长，要解码输出才能验证
		(FFmpegProgress remuxedProgress, int remuxedFrames) = await DecodeAsync(runner, remuxed, cancellationToken);
		await Assert.That(remuxedProgress.Duration ?? TimeSpan.Zero).IsBetween(SampleDuration - Tolerance, SampleDuration + Tolerance);
		await Assert.That(remuxedFrames).IsEqualTo((int)(SampleDuration.TotalSeconds * 10));

		string cut = directory.Combine(@"cut.mkv");
		await runner.RunAsync(FFmpegArguments.Cut(remuxed, cut, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1)), null, cancellationToken);
		(_, int cutFrames) = await DecodeAsync(runner, cut, cancellationToken);

		// 只有第 0 帧是关键帧，起点退到 0 秒；-to 按绝对时间约 12 帧，被当成时长会到约 17 帧
		await Assert.That(cutFrames).IsBetween(10, 13);
	}

	[Test]
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

		// 导出时 FFmpeg 用同样的规则转义
		string exported = directory.Combine(@"exported.txt");
		await runner.RunAsync([@"-hide_banner", @"-y", @"-i", mp4, @"-f", @"ffmetadata", exported], null, cancellationToken);
		string metadata = await File.ReadAllTextAsync(exported, cancellationToken);
		(_, int frames) = await DecodeAsync(runner, mp4, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(metadata).Contains("title=直播标题\n").And.Contains("title=高能：a\\=b\\;c\\#d\\\\e\n");
			await Assert.That(metadata.Split(@"[CHAPTER]")).Count().IsEqualTo(3);
			await Assert.That(frames).IsEqualTo(20);
		}
	}

	[Test]
	public async Task ChapterTitlesEndingWithBackslashKeepTheirBoundariesAsync(CancellationToken cancellationToken)
	{
		FFmpegRunner runner = CreateRunner();
		using TestDirectory directory = new();
		string flv = directory.Combine(@"sample.flv");
		await GenerateFlvAsync(runner, flv, cancellationToken);

		string chapters = directory.Combine(@"chapters.txt");
		await FFmpegMetadata.WriteChaptersFileAsync(chapters,
		[
			new FFmpegChapter(TimeSpan.Zero, TimeSpan.FromSeconds(1), @"orz\"),
			new FFmpegChapter(TimeSpan.FromSeconds(1), SampleDuration, @"end\")
		], cancellationToken);
		string mp4 = directory.Combine(@"remuxed.mp4");
		await runner.RunAsync(FFmpegArguments.RemuxWithChapters(flv, chapters, mp4), null, cancellationToken);

		string exported = directory.Combine(@"exported.txt");
		await runner.RunAsync([@"-hide_banner", @"-y", @"-i", mp4, @"-f", @"ffmetadata", exported], null, cancellationToken);
		string metadata = await File.ReadAllTextAsync(exported, cancellationToken);

		// 标题末尾多出的空格是为了让 FFmpeg 认出行尾
		using (Assert.Multiple())
		{
			await Assert.That(metadata.Split(@"[CHAPTER]")).Count().IsEqualTo(3);
			await Assert.That(metadata).Contains("START=0\nEND=1000\ntitle=orz\\\\ \n").And.Contains("START=1000\nEND=2000\ntitle=end\\\\ \n");
		}
	}

	[Test]
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

		await Assert.That(server.RequestHeads.First())
			.StartsWith(@"GET /live.flv?sign=a%2Fb HTTP/1.1")
			.And.Contains("User-Agent: MediaToolkit-Test/1.0\r\n")
			.And.Contains("Referer: https://live.bilibili.com/\r\n");

		(_, int sourceFrames) = await DecodeAsync(runner, flv, cancellationToken);
		(FFmpegProgress recorded, int recordedFrames) = await DecodeAsync(runner, output, cancellationToken);
		await Assert.That(recordedFrames).IsEqualTo(sourceFrames);
		// MPEG-TS 和 FLV 算容器时长的方法不同，只能和采样时长比较
		await Assert.That(recorded.Duration ?? TimeSpan.Zero).IsBetween(SampleDuration - Tolerance, SampleDuration + Tolerance);
	}

	[Test]
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
