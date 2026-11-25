using MediaToolkit.FFmpeg;

namespace MediaToolkit.Tests.FFmpeg;

public class FFmpegArgumentsTest
{
	public static IEnumerable<TestDataRow<Func<(IReadOnlyList<string> Arguments, string[] Expected)>>> Commands()
	{
		yield return new
		(
			static () => (FFmpegArguments.RemuxWithChapters(@"in put.flv", @"chap ters.txt", @"out put.mp4"),
				[@"-hide_banner", @"-y", @"-i", @"in put.flv", @"-f", @"ffmetadata", @"-i", @"chap ters.txt", @"-map", @"0", @"-map_chapters", @"1", @"-c", @"copy", @"-copyinkf:a", @"out put.mp4"]),
			DisplayName: "Remux with chapters takes streams from the first input and chapters from the second"
		);
		yield return new
		(
			static () => (FFmpegArguments.Cut(@"in.flv", @"out.mp4", new TimeSpan(0, 1, 2, 3, 456), new TimeSpan(1, 2, 3, 4, 5)),
				[@"-hide_banner", @"-y", @"-ss", @"01:02:03.456", @"-to", @"26:03:04.005", @"-i", @"in.flv", @"-map", @"0", @"-c", @"copy", @"-copyinkf:a", @"-avoid_negative_ts", @"1", @"out.mp4"]),
			DisplayName: "Cut seeks on the input and counts hours past a day"
		);
		yield return new
		(
			static () => (FFmpegArguments.Cut(@"in.mp4", @"out.mp4", TimeSpan.Zero, TimeSpan.FromSeconds(1)),
				[@"-hide_banner", @"-y", @"-to", @"00:00:01.000", @"-i", @"in.mp4", @"-map", @"0", @"-c", @"copy", @"-copyinkf:a", @"-avoid_negative_ts", @"1", @"out.mp4"]),
			DisplayName: "Cut from the start does not seek"
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
				[@"-hide_banner", @"-y", @"-framerate", @"30", @"-i", @"v.h264", @"-map", @"0", @"-c", @"copy", @"-copyinkf:a", @"out.mp4"]),
			DisplayName: "Remux passes the frame rate before the raw video input"
		);
	}

	[Test]
	[MethodDataSource(nameof(Commands))]
	public async Task BuildsTheExpectedArgumentsAsync(IReadOnlyList<string> arguments, string[] expected)
	{
		await Assert.That(arguments).IsEquivalentTo(expected, CollectionOrdering.Matching);
	}
}
