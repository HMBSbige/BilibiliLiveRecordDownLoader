using System.Globalization;

namespace MediaToolkit.FFmpeg;

public static class FFmpegArguments
{
	public static IReadOnlyList<string> Remux(string input, string output, double? inputFrameRate = null)
	{
		return [@"-hide_banner", @"-y", .. FrameRate(inputFrameRate), @"-i", input, @"-map", @"0", @"-c", @"copy", output];
	}

	public static IReadOnlyList<string> Cut(string input, string output, TimeSpan start, TimeSpan end)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(start, TimeSpan.Zero);
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(end, start);

		return
		[
			@"-hide_banner", @"-y",
			@"-ss", FormatTime(start), @"-to", FormatTime(end), @"-accurate_seek",
			@"-i", input,
			@"-map", @"0", @"-c", @"copy", @"-avoid_negative_ts", @"1",
			output
		];
	}

	public static IReadOnlyList<string> Merge(string video, string audio, string output, double? videoFrameRate = null)
	{
		return [@"-hide_banner", @"-y", .. FrameRate(videoFrameRate), @"-i", video, @"-i", audio, @"-map", @"0:v:0", @"-map", @"1:a:0", @"-c", @"copy", output];
	}

	// 以 MPEG-TS 写到标准输出，由调用方写盘
	public static IReadOnlyList<string> Record(Uri source, string? userAgent, Uri? referrer, TimeSpan ioTimeout, FillerRemoval fillerRemoval = FillerRemoval.None)
	{
		if (!source.IsAbsoluteUri)
		{
			throw new ArgumentException(@"Source must be an absolute URI.", nameof(source));
		}

		List<string> arguments = [@"-hide_banner"];

		if (!string.IsNullOrEmpty(userAgent))
		{
			arguments.AddRange([@"-user_agent", userAgent]);
		}

		if (referrer is not null)
		{
			arguments.AddRange([@"-headers", $"Referer: {referrer.AbsoluteUri}\r\n"]);
		}

		if (ioTimeout != Timeout.InfiniteTimeSpan)
		{
			ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ioTimeout, TimeSpan.Zero);
			arguments.AddRange([@"-rw_timeout", ((long)ioTimeout.TotalMicroseconds).ToString(CultureInfo.InvariantCulture)]);
		}

		// 签名过的直播地址必须原样传递，AbsoluteUri 会重新规范化转义
		arguments.AddRange([@"-i", source.OriginalString, @"-c", @"copy"]);

		// filter_units 只按 NAL 头拆分，不解析内容；h264_metadata 的 delete_filler 会完整解析每个 NAL，遇到解析不了的 SEI 会丢掉整帧
		string? fillerFilter = fillerRemoval switch
		{
			FillerRemoval.Avc => @"filter_units=remove_types=12",
			FillerRemoval.Hevc => @"filter_units=remove_types=38",
			_ => null
		};

		if (fillerFilter is not null)
		{
			arguments.AddRange([@"-bsf:v", fillerFilter]);
		}

		arguments.AddRange([@"-f", @"mpegts", @"pipe:1"]);

		return arguments;
	}

	// 裸流输入没有时间戳，由 -framerate 决定帧间隔
	private static string[] FrameRate(double? frameRate)
	{
		return frameRate is double rate and > 0 ? [@"-framerate", rate.ToString(@"0.###", CultureInfo.InvariantCulture)] : [];
	}

	// TimeSpan 的 hh 只是小时分量，超过一天会丢掉天数
	internal static string FormatTime(TimeSpan value)
	{
		return string.Create(CultureInfo.InvariantCulture, $@"{(long)value.TotalHours:00}:{value:mm\:ss\.fff}");
	}
}
