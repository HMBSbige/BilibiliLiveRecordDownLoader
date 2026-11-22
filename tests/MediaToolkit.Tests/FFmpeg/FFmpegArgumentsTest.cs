using MediaToolkit.FFmpeg;

namespace MediaToolkit.Tests.FFmpeg;

public class FFmpegArgumentsTest
{
	[Test]
	public async Task RemuxCopiesAllStreamsAsync()
	{
		IReadOnlyList<string> arguments = FFmpegArguments.Remux(@"in put.flv", @"out put.mp4");

		string[] expected = [@"-hide_banner", @"-y", @"-i", @"in put.flv", @"-map", @"0", @"-c", @"copy", @"out put.mp4"];
		await Assert.That(arguments).IsEquivalentTo(expected, StringComparer.Ordinal, CollectionOrdering.Matching);
	}

	[Test]
	public async Task CutSeeksOnInputAndCopiesAsync()
	{
		IReadOnlyList<string> arguments = FFmpegArguments.Cut(@"in.flv", @"out.mp4", new TimeSpan(0, 1, 2, 3, 456), new TimeSpan(1, 2, 3, 4, 5));

		string[] expected =
		[
			@"-hide_banner", @"-y",
			@"-ss", @"01:02:03.456", @"-to", @"26:03:04.005", @"-accurate_seek",
			@"-i", @"in.flv",
			@"-map", @"0", @"-c", @"copy", @"-avoid_negative_ts", @"1",
			@"out.mp4"
		];
		await Assert.That(arguments).IsEquivalentTo(expected, StringComparer.Ordinal, CollectionOrdering.Matching);
	}

	[Test]
	[Arguments(-1, 1000)]
	[Arguments(1000, 1000)]
	[Arguments(2000, 1000)]
	public async Task CutRejectsInvalidRangeAsync(int startMilliseconds, int endMilliseconds)
	{
		await Assert.That(() => FFmpegArguments.Cut(@"in.flv", @"out.mp4", TimeSpan.FromMilliseconds(startMilliseconds), TimeSpan.FromMilliseconds(endMilliseconds))).Throws<ArgumentOutOfRangeException>();
	}

	[Test]
	public async Task FrameRateIsPassedBeforeTheRawVideoInputAsync()
	{
		IReadOnlyList<string> merge = FFmpegArguments.Merge(@"v.h264", @"a.aac", @"out.mp4", 59.94);
		IReadOnlyList<string> remux = FFmpegArguments.Remux(@"v.h264", @"out.mp4", 30);

		string[] expectedMerge = [@"-hide_banner", @"-y", @"-framerate", @"59.94", @"-i", @"v.h264", @"-i", @"a.aac", @"-map", @"0:v:0", @"-map", @"1:a:0", @"-c", @"copy", @"out.mp4"];
		string[] expectedRemux = [@"-hide_banner", @"-y", @"-framerate", @"30", @"-i", @"v.h264", @"-map", @"0", @"-c", @"copy", @"out.mp4"];
		await Assert.That(merge).IsEquivalentTo(expectedMerge, StringComparer.Ordinal, CollectionOrdering.Matching);
		await Assert.That(remux).IsEquivalentTo(expectedRemux, StringComparer.Ordinal, CollectionOrdering.Matching);
	}

	[Test]
	public async Task MergeTakesVideoFromFirstInputAndAudioFromSecondAsync()
	{
		IReadOnlyList<string> arguments = FFmpegArguments.Merge(@"v.h264", @"a.aac", @"out.mp4");

		string[] expected = [@"-hide_banner", @"-y", @"-i", @"v.h264", @"-i", @"a.aac", @"-map", @"0:v:0", @"-map", @"1:a:0", @"-c", @"copy", @"out.mp4"];
		await Assert.That(arguments).IsEquivalentTo(expected, StringComparer.Ordinal, CollectionOrdering.Matching);
	}

	[Test]
	public async Task RecordPassesHttpOptionsBeforeInputAsync()
	{
		Uri source = new(@"https://cn-gotcha.bilivideo.com/live/x.flv?expires=1&sign=a%2Fb~c");

		IReadOnlyList<string> arguments = FFmpegArguments.Record(source, @"Mozilla/5.0 (Test)", new Uri(@"https://live.bilibili.com/"), TimeSpan.FromSeconds(10.5));

		string[] expected =
		[
			@"-hide_banner",
			@"-user_agent", @"Mozilla/5.0 (Test)",
			@"-headers", "Referer: https://live.bilibili.com/\r\n",
			@"-rw_timeout", @"10500000",
			@"-i", @"https://cn-gotcha.bilivideo.com/live/x.flv?expires=1&sign=a%2Fb~c",
			@"-c", @"copy", @"-f", @"mpegts", @"pipe:1"
		];
		await Assert.That(arguments).IsEquivalentTo(expected, StringComparer.Ordinal, CollectionOrdering.Matching);
	}

	[Test]
	public async Task RecordOmitsUnsetOptionsAsync()
	{
		IReadOnlyList<string> arguments = FFmpegArguments.Record(new Uri(@"https://example.com/live.m3u8"), null, null, Timeout.InfiniteTimeSpan);

		string[] expected = [@"-hide_banner", @"-i", @"https://example.com/live.m3u8", @"-c", @"copy", @"-f", @"mpegts", @"pipe:1"];
		await Assert.That(arguments).IsEquivalentTo(expected, StringComparer.Ordinal, CollectionOrdering.Matching);
	}

	[Test]
	[Arguments(FillerRemoval.Avc, "filter_units=remove_types=12")]
	[Arguments(FillerRemoval.Hevc, "filter_units=remove_types=38")]
	public async Task RecordRemovesFillerWithABitstreamFilterAfterTheCodecAsync(FillerRemoval fillerRemoval, string filter)
	{
		IReadOnlyList<string> arguments = FFmpegArguments.Record(new Uri(@"https://example.com/live.flv"), null, null, Timeout.InfiniteTimeSpan, fillerRemoval);

		string[] expected = [@"-hide_banner", @"-i", @"https://example.com/live.flv", @"-c", @"copy", @"-bsf:v", filter, @"-f", @"mpegts", @"pipe:1"];
		await Assert.That(arguments).IsEquivalentTo(expected, StringComparer.Ordinal, CollectionOrdering.Matching);
	}

	[Test]
	public async Task RecordKeepsFillerWhenNotAskedAsync()
	{
		IReadOnlyList<string> arguments = FFmpegArguments.Record(new Uri(@"https://example.com/live.flv"), null, null, Timeout.InfiniteTimeSpan, FillerRemoval.None);

		await Assert.That(arguments).DoesNotContain(@"-bsf:v");
	}

	[Test]
	public async Task RecordRejectsRelativeSourceAsync()
	{
		await Assert.That(() => FFmpegArguments.Record(new Uri(@"live.flv", UriKind.Relative), null, null, Timeout.InfiniteTimeSpan)).Throws<ArgumentException>();
	}

	[Test]
	public async Task NoCommandDisablesStandardInputAsync()
	{
		Uri source = new(@"https://example.com/live.flv");
		IReadOnlyList<string>[] commands =
		[
			FFmpegArguments.Remux(@"a", @"b"),
			FFmpegArguments.Cut(@"a", @"b", TimeSpan.Zero, TimeSpan.FromSeconds(1)),
			FFmpegArguments.Merge(@"a", @"b", @"c"),
			FFmpegArguments.Record(source, null, null, Timeout.InfiniteTimeSpan)
		];

		// -nostdin 会让取消时写入的 q 失效
		await Assert.That(commands.SelectMany(static command => command)).DoesNotContain(@"-nostdin");
	}
}
