using MediaToolkit.FFmpeg;

namespace MediaToolkit.Tests.FFmpeg;

public class FFmpegArgumentsTest
{
	private static readonly Uri Source = new(@"https://example.com/live.flv");

	public static IEnumerable<TestDataRow<Func<(IReadOnlyList<string> Arguments, string[] Expected)>>> Commands()
	{
		yield return new(static () => (FFmpegArguments.Remux(@"in put.flv", @"out put.mp4"),
			[@"-hide_banner", @"-y", @"-i", @"in put.flv", @"-map", @"0", @"-c", @"copy", @"out put.mp4"]),
			DisplayName: "Remux copies all streams");
		yield return new(static () => (FFmpegArguments.RemuxWithChapters(@"in put.flv", @"chap ters.txt", @"out put.mp4"),
			[@"-hide_banner", @"-y", @"-i", @"in put.flv", @"-f", @"ffmetadata", @"-i", @"chap ters.txt", @"-map", @"0", @"-map_chapters", @"1", @"-c", @"copy", @"out put.mp4"]),
			DisplayName: "Remux with chapters takes streams from the first input and chapters from the second");
		yield return new(static () => (FFmpegArguments.Cut(@"in.flv", @"out.mp4", new TimeSpan(0, 1, 2, 3, 456), new TimeSpan(1, 2, 3, 4, 5)),
			[@"-hide_banner", @"-y", @"-ss", @"01:02:03.456", @"-to", @"26:03:04.005", @"-i", @"in.flv", @"-map", @"0", @"-c", @"copy", @"-avoid_negative_ts", @"1", @"out.mp4"]),
			DisplayName: "Cut seeks on the input and counts hours past a day");
		yield return new(static () => (FFmpegArguments.Merge(@"v.h264", @"a.aac", @"out.mp4", 59.94),
			[@"-hide_banner", @"-y", @"-framerate", @"59.94", @"-i", @"v.h264", @"-i", @"a.aac", @"-map", @"0:v:0", @"-map", @"1:a:0", @"-c", @"copy", @"out.mp4"]),
			DisplayName: "Merge passes the frame rate before the raw video input");
		yield return new(static () => (FFmpegArguments.Remux(@"v.h264", @"out.mp4", 30),
			[@"-hide_banner", @"-y", @"-framerate", @"30", @"-i", @"v.h264", @"-map", @"0", @"-c", @"copy", @"out.mp4"]),
			DisplayName: "Remux passes the frame rate before the raw video input");
		yield return new(static () => (FFmpegArguments.Record(new Uri(@"https://cn-gotcha.bilivideo.com/live/x.flv?expires=1&sign=a%2Fb~c"), @"Mozilla/5.0 (Test)", new Uri(@"https://live.bilibili.com/"), TimeSpan.FromSeconds(10.5)),
			[@"-hide_banner", @"-user_agent", @"Mozilla/5.0 (Test)", @"-headers", "Referer: https://live.bilibili.com/\r\n", @"-rw_timeout", @"10500000", @"-i", @"https://cn-gotcha.bilivideo.com/live/x.flv?expires=1&sign=a%2Fb~c", @"-c", @"copy", @"-f", @"mpegts", @"pipe:1"]),
			DisplayName: "Record passes HTTP options before the signed input");
		// -http_proxy 是输入的协议选项，放在 -i 后面就成了输出选项
		yield return new(static () => (FFmpegArguments.Record(Source, null, null, Timeout.InfiniteTimeSpan, FillerRemoval.None, new Uri(@"http://127.0.0.1:7890")),
			[@"-hide_banner", @"-http_proxy", @"http://127.0.0.1:7890/", @"-i", @"https://example.com/live.flv", @"-c", @"copy", @"-f", @"mpegts", @"pipe:1"]),
			DisplayName: "Record passes the HTTP proxy before the input");
		yield return new(static () => (FFmpegArguments.Record(new Uri(@"https://example.com/live.m3u8"), null, null, Timeout.InfiniteTimeSpan),
			[@"-hide_banner", @"-i", @"https://example.com/live.m3u8", @"-c", @"copy", @"-f", @"mpegts", @"pipe:1"]),
			DisplayName: "Record omits unset options");
		yield return new(static () => (FFmpegArguments.Record(Source, null, null, Timeout.InfiniteTimeSpan, FillerRemoval.Avc),
			[@"-hide_banner", @"-i", @"https://example.com/live.flv", @"-c", @"copy", @"-bsf:v", @"filter_units=remove_types=12", @"-f", @"mpegts", @"pipe:1"]),
			DisplayName: "Record removes H.264 filler after the codec");
		yield return new(static () => (FFmpegArguments.Record(Source, null, null, Timeout.InfiniteTimeSpan, FillerRemoval.Hevc),
			[@"-hide_banner", @"-i", @"https://example.com/live.flv", @"-c", @"copy", @"-bsf:v", @"filter_units=remove_types=38", @"-f", @"mpegts", @"pipe:1"]),
			DisplayName: "Record removes HEVC filler after the codec");
	}

	[Test]
	[MethodDataSource(nameof(Commands))]
	public async Task BuildsTheExpectedArgumentsAsync(IReadOnlyList<string> arguments, string[] expected)
	{
		await Assert.That(arguments).IsEquivalentTo(expected, CollectionOrdering.Matching);
	}

	[Test]
	[Arguments(-1, 1000)]
	[Arguments(1000, 1000)]
	public async Task CutRejectsInvalidRangeAsync(int startMilliseconds, int endMilliseconds)
	{
		await Assert.That(() => FFmpegArguments.Cut(@"in.flv", @"out.mp4", TimeSpan.FromMilliseconds(startMilliseconds), TimeSpan.FromMilliseconds(endMilliseconds))).Throws<ArgumentOutOfRangeException>();
	}

	[Test]
	public async Task RecordRejectsAProxyFFmpegWouldIgnoreAsync()
	{
		await Assert.That(static () => FFmpegArguments.Record(Source, null, null, Timeout.InfiniteTimeSpan, FillerRemoval.None, new Uri(@"https://127.0.0.1:7890"))).Throws<ArgumentException>();
	}

	[Test]
	public async Task RecordRejectsRelativeSourceAsync()
	{
		await Assert.That(static () => FFmpegArguments.Record(new Uri(@"live.flv", UriKind.Relative), null, null, Timeout.InfiniteTimeSpan)).Throws<ArgumentException>();
	}
}
