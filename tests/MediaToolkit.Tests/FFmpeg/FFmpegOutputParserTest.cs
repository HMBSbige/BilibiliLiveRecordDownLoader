using MediaToolkit.FFmpeg;

namespace MediaToolkit.Tests.FFmpeg;

public class FFmpegOutputParserTest
{
	[Test]
	[Arguments(@"ffmpeg version 9.1-full_build-example.test Copyright (c) 2000-2026", @"9.1-full_build-example.test")]
	public async Task ParseVersionExtractsVersionTokenAsync(string output, string expected)
	{
		await Assert.That(FFmpegOutputParser.ParseVersion(output + "\nbuilt with gcc")).IsEqualTo(expected);
	}

	[Test]
	[Arguments(@"ffprobe version 7.1 Copyright")]
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
	[Arguments(@"size=N/A time=100:00:00.00", 3_600_000_000_000L)]
	[Arguments(@"size=N/A time=00:00:01.5", 15_000_000L)]
	[Arguments(@"size=N/A time=00:00:01.123456789", 11_234_567L)]
	[Arguments(@"size=N/A time=00:00:07", 70_000_000L)]
	[Arguments(@"size=N/A time=-00:00:00.02", -200_000L)]
	public async Task ParseProgressHandlesTimeFormatsAsync(string line, long expectedTicks)
	{
		FFmpegProgress progress = FFmpegOutputParser.ParseProgress(line, default);

		await Assert.That(progress.Position).IsEqualTo(TimeSpan.FromTicks(expectedTicks));
	}

	[Test]
	[Arguments(@"  Duration: N/A, start: 0.000000, bitrate: N/A")]
	[Arguments(@"frame=    0 fps=0.0 q=0.0 size=       0KiB time=N/A bitrate=N/A speed=N/A")]
	[Arguments(@"frame=    0 fps=0.0 q=0.0 size=       0KiB time=-2562047788:00:54.77 bitrate=N/A speed=N/A")]
	[Arguments(@"        title           : Duration: 10:00:00.00")]
	[Arguments(@"        title           : frame=1 time=00:00:01.00")]
	public async Task ParseProgressKeepsPreviousValuesWhenUnavailableAsync(string line)
	{
		FFmpegProgress previous = new(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(2), @"previous");

		FFmpegProgress progress = FFmpegOutputParser.ParseProgress(line, previous);

		await Assert.That(progress).IsEqualTo(previous with { Message = line });
	}

	[Test]
	[Arguments(@"frame=   20 fps=0.0 q=-1.0 Lsize=N/A time=N/A bitrate=N/A speed=N/A", false)]
	[Arguments(@"size=       0KiB time=00:00:01.00 bitrate=   0.0kbits/s speed=1x", false)]
	[Arguments(@"Conversion failed!", false)]
	[Arguments(@"Exiting normally, received signal 2.", false)]
	[Arguments(@"[out#0/wav @ 000001] Could not write header (incorrect codec parameters ?): Invalid argument", true)]
	public async Task IsDiagnosticSkipsStatisticsAndClosingNoticesAsync(string line, bool expected)
	{
		await Assert.That(FFmpegOutputParser.IsDiagnostic(line)).IsEqualTo(expected);
	}
}
