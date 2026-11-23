using MediaToolkit.FFmpeg;

namespace MediaToolkit.Tests.FFmpeg;

public class FFmpegOutputParserTest
{
	[Test]
	[Arguments(@"ffmpeg version 7.1.1 Copyright (c) 2000-2025 the FFmpeg developers", @"7.1.1")]
	[Arguments(@"ffmpeg version 9.1-full_build-example.test Copyright (c) 2000-2026", @"9.1-full_build-example.test")]
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
	[Arguments(@"time=100:00:00.00", 3_600_000_000_000L)]
	[Arguments(@"time=00:00:01.5", 15_000_000L)]
	[Arguments(@"time=00:00:01.123456789", 11_234_567L)]
	[Arguments(@"time=00:00:07", 70_000_000L)]
	[Arguments(@"time=-00:00:00.02", -200_000L)]
	public async Task ParseProgressHandlesTimeFormatsAsync(string line, long expectedTicks)
	{
		FFmpegProgress progress = FFmpegOutputParser.ParseProgress(line, default);

		await Assert.That(progress.Position).IsEqualTo(TimeSpan.FromTicks(expectedTicks));
	}

	[Test]
	[Arguments(@"  Duration: N/A, start: 0.000000, bitrate: N/A")]
	[Arguments(@"frame=    0 fps=0.0 q=0.0 size=       0KiB time=N/A bitrate=N/A speed=N/A")]
	[Arguments(@"[flv @ 000001] Packet mismatch")]
	// FFmpeg 会原样打印章节标题和元数据，里面的时间写法可能解析不了
	[Arguments(@"        title           : Duration: ０1:00:00")]
	[Arguments(@"        title           : time=００:００:０１")]
	[Arguments(@"        title           : time=99999999999999999999:00:00")]
	public async Task ParseProgressKeepsPreviousValuesWhenUnavailableAsync(string line)
	{
		FFmpegProgress previous = new(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(2), @"previous");

		FFmpegProgress progress = FFmpegOutputParser.ParseProgress(line, previous);

		await Assert.That(progress).IsEqualTo(previous with { Message = line });
	}

	[Test]
	[Arguments(@"Error opening input file https://cdn.test/live/a.flv?expires=1&sign=abc%2F&uid=42.", @"Error opening input file https://cdn.test/live/a.flv?….")]
	[Arguments(@"https://cdn.test/live/a.flv?sign=abc&uid=42: Server returned 403 Forbidden", @"https://cdn.test/live/a.flv?…: Server returned 403 Forbidden")]
	[Arguments(@"[hls @ 0000020] Opening 'https://cdn.test/live/1.m4s?sign=abc' for reading", @"[hls @ 0000020] Opening 'https://cdn.test/live/1.m4s?…' for reading")]
	[Arguments(@"Input #0, flv, from ""http://cdn.test/a.flv?sign=a.b:c"":", @"Input #0, flv, from ""http://cdn.test/a.flv?…"":")]
	[Arguments(@"a http://x.test/1?sign=1 and http://y.test/2?sign=2", @"a http://x.test/1?… and http://y.test/2?…")]
	[Arguments(@"[tcp @ 000001] Connection to tcp://127.0.0.1:9 failed", @"[tcp @ 000001] Connection to tcp://127.0.0.1:9 failed")]
	[Arguments(@"Is this a question?", @"Is this a question?")]
	public async Task RedactUrlQueriesKeepsOnlySchemeHostAndPathAsync(string line, string expected)
	{
		await Assert.That(FFmpegOutputParser.RedactUrlQueries(line)).IsEqualTo(expected);
	}

	[Test]
	[Arguments(@"frame=   20 fps=0.0 q=-1.0 Lsize=N/A time=N/A bitrate=N/A speed=N/A", false)]
	[Arguments(@"size=       0KiB time=00:00:01.00 bitrate=   0.0kbits/s speed=1x", false)]
	[Arguments(@"Conversion failed!", false)]
	[Arguments(@"Exiting normally, received signal 2.", false)]
	[Arguments(@"[out#0/wav @ 000001] Could not write header (incorrect codec parameters ?): Invalid argument", true)]
	[Arguments(@"  Stream #0:0 -> #0:0 (copy)", true)]
	public async Task IsDiagnosticSkipsStatisticsAndClosingNoticesAsync(string line, bool expected)
	{
		await Assert.That(FFmpegOutputParser.IsDiagnostic(line)).IsEqualTo(expected);
	}
}
