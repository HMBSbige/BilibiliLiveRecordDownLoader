using MediaToolkit.FFmpeg;

namespace MediaToolkit.Tests.FFmpeg;

public class FFmpegArgumentsTest
{
	private static readonly Uri Source = new(@"https://example.com/live.flv");

	private static readonly string[] RecordOutput = [@"-f", @"mp4", @"-movflags", @"frag_keyframe+empty_moov+default_base_moof+skip_trailer", @"-frag_duration", @"1000000", @"pipe:1"];

	public static IEnumerable<TestDataRow<Func<(IReadOnlyList<string> Arguments, string[] Expected)>>> Commands()
	{
		yield return new
		(
			static () => (FFmpegArguments.RemuxWithChapters(@"in put.flv", @"chap ters.txt", @"out put.mp4"),
				[@"-hide_banner", @"-y", @"-i", @"in put.flv", @"-f", @"ffmetadata", @"-i", @"chap ters.txt", @"-map", @"0", @"-map_chapters", @"1", @"-c", @"copy", @"out put.mp4"]),
			DisplayName: "Remux with chapters takes streams from the first input and chapters from the second"
		);
		yield return new
		(
			static () => (FFmpegArguments.Cut(@"in.flv", @"out.mp4", new TimeSpan(0, 1, 2, 3, 456), new TimeSpan(1, 2, 3, 4, 5)),
				[@"-hide_banner", @"-y", @"-ss", @"01:02:03.456", @"-to", @"26:03:04.005", @"-i", @"in.flv", @"-map", @"0", @"-c", @"copy", @"-avoid_negative_ts", @"1", @"out.mp4"]),
			DisplayName: "Cut seeks on the input and counts hours past a day"
		);
		yield return new
		(
			static () => (FFmpegArguments.Merge(@"v.h264", @"a.aac", @"out.mp4", 59.94),
				[@"-hide_banner", @"-y", @"-framerate", @"59.94", @"-i", @"v.h264", @"-i", @"a.aac", @"-map", @"0:v:0", @"-map", @"1:a:0", @"-c", @"copy", @"out.mp4"]),
			DisplayName: "Merge passes the frame rate before the raw video input"
		);
		yield return new
		(
			static () => (FFmpegArguments.Remux(@"v.h264", @"out.mp4", 30),
				[@"-hide_banner", @"-y", @"-framerate", @"30", @"-i", @"v.h264", @"-map", @"0", @"-c", @"copy", @"out.mp4"]),
			DisplayName: "Remux passes the frame rate before the raw video input"
		);
		yield return new
		(
			static () => (FFmpegArguments.Record(new Uri(@"https://cn-gotcha.bilivideo.com/live/x.flv?expires=1&sign=a%2Fb~c"), VideoCodec.Av1, @"Mozilla/5.0 (Test)", new Uri(@"https://live.bilibili.com/"), TimeSpan.FromSeconds(10.5)),
				[@"-hide_banner", @"-user_agent", @"Mozilla/5.0 (Test)", @"-headers", "Referer: https://live.bilibili.com/\r\n", @"-rw_timeout", @"10500000", @"-i", @"https://cn-gotcha.bilivideo.com/live/x.flv?expires=1&sign=a%2Fb~c", @"-c", @"copy", .. RecordOutput]),
			DisplayName: "Record passes HTTP options before the signed input"
		);
		yield return new
		(
			static () => (FFmpegArguments.Record(Source, VideoCodec.Av1, null, null, Timeout.InfiniteTimeSpan, new Uri(@"http://127.0.0.1:7890")),
				[@"-hide_banner", @"-http_proxy", @"http://127.0.0.1:7890/", @"-i", @"https://example.com/live.flv", @"-c", @"copy", .. RecordOutput]),
			DisplayName: "Record passes the HTTP proxy before the input"
		);
		yield return new
		(
			static () => (FFmpegArguments.Record(Source, VideoCodec.Av1, null, null, Timeout.InfiniteTimeSpan, isTransportStream: true),
				[@"-hide_banner", @"-i", @"https://example.com/live.flv", @"-c", @"copy", @"-bsf:a", @"aac_adtstoasc", .. RecordOutput]),
			DisplayName: "Record strips the ADTS headers of AAC from a transport stream"
		);
		yield return new
		(
			static () => (FFmpegArguments.Record(Source, VideoCodec.Avc, null, null, Timeout.InfiniteTimeSpan),
				[@"-hide_banner", @"-i", @"https://example.com/live.flv", @"-c", @"copy", @"-bsf:v", @"h264_mp4toannexb", @"-tag:v", @"avc3", .. RecordOutput]),
			DisplayName: "Record keeps H.264 parameter sets in the samples"
		);
		yield return new
		(
			static () => (FFmpegArguments.Record(Source, VideoCodec.Hevc, null, null, Timeout.InfiniteTimeSpan, isTransportStream: true),
				[@"-hide_banner", @"-i", @"https://example.com/live.flv", @"-c", @"copy", @"-bsf:v", @"hevc_mp4toannexb", @"-tag:v", @"hev1", @"-bsf:a", @"aac_adtstoasc", .. RecordOutput]),
			DisplayName: "Record keeps HEVC parameter sets in the samples"
		);
	}

	[Test]
	[MethodDataSource(nameof(Commands))]
	public async Task BuildsTheExpectedArgumentsAsync(IReadOnlyList<string> arguments, string[] expected)
	{
		await Assert.That(arguments).IsEquivalentTo(expected, CollectionOrdering.Matching);
	}

	[Test]
	public async Task RecordRejectsAProxyFFmpegWouldIgnoreAsync()
	{
		await Assert.That(static () => FFmpegArguments.Record(Source, VideoCodec.Av1, null, null, Timeout.InfiniteTimeSpan, new Uri(@"https://127.0.0.1:7890"))).Throws<ArgumentException>();
	}
}
