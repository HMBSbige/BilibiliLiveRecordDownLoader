using System.Globalization;

namespace MediaToolkit.FFmpeg;

// 都不能带 -nostdin：取消时 FFmpegRunner 往 stdin 写 q 让 FFmpeg 收尾
public static class FFmpegArguments
{
	// fMP4 可能把第一个分片的音频样本标成非同步，-c copy 会丢掉每路流第一个同步样本之前的包；AAC 每帧都能单独解码，所以音频照样复制，视频关键帧之前的帧解不出来，仍然丢掉
	private static readonly string[] CopyStreams = [@"-c", @"copy", @"-copyinkf:a"];

	public static IReadOnlyList<string> Remux(string input, string output, double? inputFrameRate = null)
	{
		return [@"-hide_banner", @"-y", .. FrameRate(inputFrameRate), @"-i", input, @"-map", @"0", .. CopyStreams, output];
	}

	// 不靠内容探测：带 BOM 或缺 ;FFMETADATA1 头的章节文件也能读
	public static IReadOnlyList<string> RemuxWithChapters(string input, string chapters, string output)
	{
		return [@"-hide_banner", @"-y", @"-i", input, @"-f", @"ffmetadata", @"-i", chapters, @"-map", @"0", @"-map_chapters", @"1", .. CopyStreams, output];
	}

	public static IReadOnlyList<string> Cut(string input, string output, TimeSpan start, TimeSpan end)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(start, TimeSpan.Zero);
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(end, start);

		return
		[
			@"-hide_banner", @"-y",
			@"-ss", FormatTime(start), @"-to", FormatTime(end),
			@"-i", input,
			@"-map", @"0", @"-c", @"copy", @"-avoid_negative_ts", @"1",
			output
		];
	}

	public static IReadOnlyList<string> Merge(string video, string audio, string output, double? videoFrameRate = null)
	{
		return [@"-hide_banner", @"-y", .. FrameRate(videoFrameRate), @"-i", video, @"-i", audio, @"-map", @"0:v:0", @"-map", @"1:a:0", @"-c", @"copy", output];
	}

	// 裸流输入没有时间戳，不给 -framerate 时 FFmpeg 按 25 帧算
	private static string[] FrameRate(double? frameRate)
	{
		return frameRate is { } rate and > 0 ? [@"-framerate", rate.ToString(@"0.###", CultureInfo.InvariantCulture)] : [];
	}

	// TimeSpan 的 hh 只是小时分量，超过一天会丢掉天数
	private static string FormatTime(TimeSpan value)
	{
		return string.Create(CultureInfo.InvariantCulture, $@"{(long)value.TotalHours:00}:{value:mm\:ss\.fff}");
	}
}
