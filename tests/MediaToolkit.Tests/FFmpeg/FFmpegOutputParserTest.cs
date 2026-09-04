using MediaToolkit.FFmpeg;

namespace MediaToolkit.Tests.FFmpeg;

public class FFmpegOutputParserTest
{
	[Test]
	[Arguments(@"ffmpeg version 7.1.1 Copyright (c) 2000-2025 the FFmpeg developers", @"7.1.1")]
	[Arguments(@"ffmpeg version 9.1-full_build-www.gyan.dev Copyright (c) 2000-2026", @"9.1-full_build-www.gyan.dev")]
	[Arguments(@"ffmpeg version N-118000-g1234abcd Copyright", @"N-118000-g1234abcd")]
	public async Task ParseVersionExtractsVersionTokenAsync(string output, string expected)
	{
		await Assert.That(FFmpegOutputParser.ParseVersion(output + "\nbuilt with gcc")).IsEqualTo(expected);
	}

	[Test]
	[Arguments(@"")]
	[Arguments(@"ffprobe version 7.1 Copyright")]
	[Arguments(@"'ffmpeg' is not recognized as an internal or external command")]
	public async Task ParseVersionReturnsNullForOtherOutputAsync(string output)
	{
		await Assert.That(FFmpegOutputParser.ParseVersion(output)).IsNull();
	}

	[Test]
	public async Task ParseProgressReadsDurationAsync()
	{
		FFmpegProgress progress = FFmpegOutputParser.ParseProgress(@"  Duration: 01:02:03.45, start: 0.000000, bitrate: 1205 kb/s", default);

		await Assert.That(progress).IsEqualTo(new FFmpegProgress(new TimeSpan(0, 1, 2, 3, 450), null, @"  Duration: 01:02:03.45, start: 0.000000, bitrate: 1205 kb/s"));
	}

	[Test]
	public async Task ParseProgressReadsPositionAndKeepsDurationAsync()
	{
		const string line = @"frame=  250 fps= 50 q=-1.0 size=    1024KiB time=00:00:10.01 bitrate= 838.0kbits/s speed=2.01x";
		FFmpegProgress previous = new(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(9), @"previous");

		FFmpegProgress progress = FFmpegOutputParser.ParseProgress(line, previous);

		await Assert.That(progress).IsEqualTo(new FFmpegProgress(TimeSpan.FromMinutes(5), TimeSpan.FromMilliseconds(10_010), line));
	}

	[Test]
	[Arguments(@"time=100:00:00.00", 360_000_000L)]
	[Arguments(@"time=00:00:01.5", 1_500L)]
	[Arguments(@"time=00:00:01.123456789", 1_123L)]
	[Arguments(@"time=00:00:07", 7_000L)]
	[Arguments(@"time=-00:00:00.02", -20L)]
	public async Task ParseProgressHandlesTimeFormatsAsync(string line, long expectedMilliseconds)
	{
		FFmpegProgress progress = FFmpegOutputParser.ParseProgress(line, default);

		await Assert.That(progress.Position?.Ticks / TimeSpan.TicksPerMillisecond).IsEqualTo(expectedMilliseconds);
	}

	[Test]
	public async Task ParseProgressReadsNanosecondFractionExactlyAsync()
	{
		FFmpegProgress progress = FFmpegOutputParser.ParseProgress(@"time=00:00:01.1234567", default);

		await Assert.That(progress.Position).IsEqualTo(TimeSpan.FromTicks(11_234_567));
	}

	[Test]
	[Arguments(@"  Duration: N/A, start: 0.000000, bitrate: N/A")]
	[Arguments(@"frame=    0 fps=0.0 q=0.0 size=       0KiB time=N/A bitrate=N/A speed=N/A")]
	[Arguments(@"[flv @ 000001] Packet mismatch")]
	public async Task ParseProgressKeepsPreviousValuesWhenUnavailableAsync(string line)
	{
		FFmpegProgress previous = new(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(2), @"previous");

		FFmpegProgress progress = FFmpegOutputParser.ParseProgress(line, previous);

		await Assert.That(progress).IsEqualTo(previous with { Message = line });
	}
}
