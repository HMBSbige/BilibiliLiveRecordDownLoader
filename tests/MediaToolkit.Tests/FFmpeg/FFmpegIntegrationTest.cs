using MediaToolkit.FFmpeg;
using MediaToolkit.Flv;
using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace MediaToolkit.Tests.FFmpeg;

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

	[GeneratedRegex(@"\] n:\s*\d+ pts:")]
	private static partial Regex AudioFrameRegex { get; }

	private static FFmpegRunner CreateRunner()
	{
		string path = FFmpegRunner.Locate(null) ?? string.Empty;
		SkipOrFail(path.Length is 0, @"FFmpeg is not installed.");
		return new FFmpegRunner(path);
	}

	private static async Task GenerateAsync(FFmpegRunner runner, string output, string format, CancellationToken cancellationToken)
	{
		string seconds = SampleDuration.TotalSeconds.ToString(CultureInfo.InvariantCulture);

		try
		{
			await runner.RunAsync
			(
				[
					@"-hide_banner", @"-y",
					@"-f", @"lavfi", @"-i", $@"testsrc=duration={seconds}:size=160x120:rate=10",
					@"-f", @"lavfi", @"-i", $@"sine=duration={seconds}",
					@"-c:v", @"libx264", @"-pix_fmt", @"yuv420p", @"-c:a", @"aac",
					@"-f", format, output
				],
				null, cancellationToken
			);
		}
		catch (FFmpegException ex)
		{
			SkipOrFail(true, $@"FFmpeg cannot encode H.264/AAC {format}: {ex.Message}");
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
	public async Task ExtractedStreamsDecodeCompletelyAsync(CancellationToken cancellationToken)
	{
		FFmpegRunner runner = CreateRunner();
		using TestDirectory directory = new();
		string flv = directory.Combine(@"sample.flv");
		await GenerateAsync(runner, flv, @"flv", cancellationToken);

		FlvExtractResult result = await FlvExtractor.ExtractAsync(flv, directory.Root, null, cancellationToken);

		string video = await Assert.That(result.VideoPath).IsNotNull();
		string audio = await Assert.That(result.AudioPath).IsNotNull();

		byte[] videoBytes = await File.ReadAllBytesAsync(video, cancellationToken);
		await Assert.That(Convert.ToHexString(videoBytes, 0, 4)).IsEqualTo(@"00000001");
		await Assert.That(videoBytes[4] & 0x1F).IsEqualTo(7);

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
		await GenerateAsync(runner, flv, @"flv", cancellationToken);

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
		await GenerateAsync(runner, flv, @"flv", cancellationToken);

		string chapters = directory.Combine(@"章节.txt");
		await FFmpegMetadata.WriteChaptersFileAsync
		(
			chapters,
			[
				new MediaChapter(TimeSpan.Zero, TimeSpan.FromSeconds(1), @"直播标题"),
				new MediaChapter(TimeSpan.FromSeconds(1), SampleDuration, @"高能：a=b;c#d\e")
			],
			cancellationToken
		);
		string mp4 = directory.Combine(@"remuxed.mp4");
		await runner.RunAsync(FFmpegArguments.RemuxWithChapters(flv, chapters, mp4), null, cancellationToken);

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
		await GenerateAsync(runner, flv, @"flv", cancellationToken);

		string chapters = directory.Combine(@"chapters.txt");
		await FFmpegMetadata.WriteChaptersFileAsync
		(
			chapters,
			[
				new MediaChapter(TimeSpan.Zero, TimeSpan.FromSeconds(1), @"orz\"),
				new MediaChapter(TimeSpan.FromSeconds(1), SampleDuration, @"end\")
			],
			cancellationToken
		);
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
	public async Task CopyingKeepsAudioBeforeTheFirstSyncSampleAsync(CancellationToken cancellationToken)
	{
		FFmpegRunner runner = CreateRunner();
		using TestDirectory directory = new();
		string mp4 = directory.Combine(@"sample.mp4");
		await GenerateAsync(runner, mp4, @"mp4", cancellationToken);

		string fragmented = directory.Combine(@"fragmented.mp4");
		await runner.RunAsync([@"-hide_banner", @"-y", @"-i", mp4, @"-c", @"copy", @"-movflags", @"frag_keyframe+empty_moov+default_base_moof", @"-frag_duration", @"500000", fragmented], null, cancellationToken);
		await File.WriteAllBytesAsync(fragmented, MarkFirstAudioFragmentNonSync(await File.ReadAllBytesAsync(fragmented, cancellationToken)), cancellationToken);
		int expected = await CountAudioFramesAsync(runner, fragmented, cancellationToken);

		string chapters = directory.Combine(@"chapters.txt");
		await FFmpegMetadata.WriteChaptersFileAsync(chapters, [new MediaChapter(TimeSpan.Zero, SampleDuration, @"直播标题")], cancellationToken);
		string remuxed = directory.Combine(@"remuxed.mp4");
		string withChapters = directory.Combine(@"chapters.mp4");
		string cut = directory.Combine(@"cut.mp4");
		await runner.RunAsync(FFmpegArguments.Remux(fragmented, remuxed), null, cancellationToken);
		await runner.RunAsync(FFmpegArguments.RemuxWithChapters(fragmented, chapters, withChapters), null, cancellationToken);
		await runner.RunAsync(FFmpegArguments.Cut(fragmented, cut, TimeSpan.Zero, SampleDuration * 2), null, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(expected).IsGreaterThan(0);
			await Assert.That(await CountAudioFramesAsync(runner, remuxed, cancellationToken)).IsEqualTo(expected);
			await Assert.That(await CountAudioFramesAsync(runner, withChapters, cancellationToken)).IsEqualTo(expected);
			await Assert.That(await CountAudioFramesAsync(runner, cut, cancellationToken)).IsEqualTo(expected);
		}

		// FFmpeg 用 tfhd 的默认样本标志把音频标成同步样本；改成非同步，相当于第一个分片的音频样本被标成非同步的 fMP4
		static byte[] MarkFirstAudioFragmentNonSync(byte[] data)
		{
			// 先输入视频再输入音频，音频是第 2 条轨
			const uint audioTrackId = 2;
			const uint defaultSampleFlagsPresent = 0x20;
			const uint nonSyncSample = 0x01010000;

			(int moof, int moofSize) = Children(data, 0, data.Length, "moof"u8)[0];

			foreach ((int traf, int trafSize) in Children(data, moof + 8, moof + moofSize, "traf"u8))
			{
				(int tfhd, _) = Children(data, traf + 8, traf + trafSize, "tfhd"u8)[0];
				uint flags = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(tfhd + 8)) & 0xFFFFFF;

				if (BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(tfhd + 12)) != audioTrackId)
				{
					continue;
				}

				if ((flags & defaultSampleFlagsPresent) is 0)
				{
					throw new InvalidDataException(@"FFmpeg no longer writes default sample flags for the audio fragment.");
				}

				// default_sample_flags 之前依次是 8 字节的 base_data_offset，以及 sample_description_index、default_sample_duration、default_sample_size 各 4 字节，都是有对应标志才出现
				int offset = tfhd + 16 + ((flags & 0x1) is 0 ? 0 : 8) + BitOperations.PopCount(flags & 0x1A) * 4;
				BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset), nonSyncSample);
				return data;
			}

			throw new InvalidDataException(@"The first fragment has no audio track.");
		}

		static List<(int Offset, int Size)> Children(byte[] data, int start, int end, ReadOnlySpan<byte> type)
		{
			List<(int Offset, int Size)> boxes = [];

			for (int offset = start; offset + 8 <= end;)
			{
				int size = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));

				if (data.AsSpan(offset + 4, 4).SequenceEqual(type))
				{
					boxes.Add((offset, size));
				}

				offset += size;
			}

			return boxes;
		}
	}

	// 解码后逐帧计数：转封装丢掉的音频包解码时也不会出现
	private static async Task<int> CountAudioFramesAsync(FFmpegRunner runner, string input, CancellationToken cancellationToken)
	{
		int frames = 0;

		await runner.RunAsync([@"-hide_banner", @"-i", input, @"-map", @"0:a", @"-af", @"ashowinfo", @"-f", @"null", @"-"], new SyncProgress<FFmpegProgress>(progress =>
		{
			if (AudioFrameRegex.IsMatch(progress.Message))
			{
				++frames;
			}
		}), cancellationToken);

		return frames;
	}

	[Test]
	public async Task CancelledRunLeavesPlayableFileAsync(CancellationToken cancellationToken)
	{
		FFmpegRunner runner = CreateRunner();
		using TestDirectory directory = new();
		string output = directory.Combine(@"live.mp4");
		using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		SyncProgress<FFmpegProgress> progress = new(p =>
		{
			if (p.Position >= TimeSpan.FromSeconds(1))
			{
				cts.Cancel();
			}
		});

		await Assert.That(() => runner.RunAsync([@"-hide_banner", @"-y", @"-re", @"-f", @"lavfi", @"-i", @"testsrc=size=160x120:rate=10", @"-c:v", @"mpeg4", output], progress, cts.Token)).Throws<OperationCanceledException>();

		(FFmpegProgress decoded, int frames) = await DecodeAsync(runner, output, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(frames).IsGreaterThanOrEqualTo(10);
			await Assert.That(decoded.Duration).IsNotNull();
		}
	}
}
