using System.Globalization;

namespace MediaToolkit.FFmpeg;

// 都不能带 -nostdin：取消时 FFmpegRunner 往 stdin 写 q 让 FFmpeg 收尾
public static class FFmpegArguments
{
	public static IReadOnlyList<string> Remux(string input, string output, double? inputFrameRate = null)
	{
		return [@"-hide_banner", @"-y", .. FrameRate(inputFrameRate), @"-i", input, @"-map", @"0", @"-c", @"copy", output];
	}

	// 不靠内容探测：带 BOM 或缺 ;FFMETADATA1 头的章节文件也能读
	public static IReadOnlyList<string> RemuxWithChapters(string input, string chapters, string output)
	{
		return [@"-hide_banner", @"-y", @"-i", input, @"-f", @"ffmetadata", @"-i", chapters, @"-map", @"0", @"-map_chapters", @"1", @"-c", @"copy", output];
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

	public static IReadOnlyList<string> Record(Uri source, VideoCodec video, string? userAgent, Uri? referrer, TimeSpan ioTimeout, Uri? httpProxy = null, bool isTransportStream = false)
	{
		if (!source.IsAbsoluteUri)
		{
			throw new ArgumentException(@"Source must be an absolute URI.", nameof(source));
		}

		// FFmpeg 只认 http:// 的代理地址，其他的会被静默忽略
		if (httpProxy is not (null or { IsAbsoluteUri: true, Scheme: @"http" }))
		{
			throw new ArgumentException(@"The proxy must be an absolute http:// URI.", nameof(httpProxy));
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

		if (httpProxy is not null)
		{
			arguments.AddRange([@"-http_proxy", httpProxy.AbsoluteUri]);
		}

		// 签名过的直播地址必须原样传递，AbsoluteUri 会重新规范化转义
		arguments.AddRange([@"-i", source.OriginalString, @"-c", @"copy"]);

		// moov 在第一个包之前就写死了，FLV 中途换分辨率时新的参数集没有地方写，之后的画面都解不出来
		// 转成 Annex B 让参数集留在关键帧前面，再用 avc3、hev1 标明样本里带参数集；AV1 的序列头本来就在样本里
		arguments.AddRange
		(
			video switch
			{
				VideoCodec.Avc => [@"-bsf:v", @"h264_mp4toannexb", @"-tag:v", @"avc3"],
				VideoCodec.Hevc => [@"-bsf:v", @"hevc_mp4toannexb", @"-tag:v", @"hev1"],
				VideoCodec.Av1 => []
			}
		);

		// empty_moov 在收到第一个包之前就写出 moov，FFmpeg 来不及按第一个包自动插入 aac_adtstoasc，TS 里带 ADTS 头的 AAC 会让它报错退出
		// 只给 TS 输入加：FLV 和 fMP4 里的 AAC 不带 ADTS 头，而这个过滤器只认 AAC，音频是别的编码时 FFmpeg 会启动失败
		if (isTransportStream)
		{
			arguments.AddRange([@"-bsf:a", @"aac_adtstoasc"]);
		}

		// MPEG-TS 封装不支持 AV1，会当作私有数据写进去，所以输出分片 MP4；管道不能回头改，moov 只能先写空的
		// frag_keyframe 让关键帧总在分片开头，录制按分片切文件；分片最长 1 秒，FFmpeg 被强行结束时丢的就是还没写出的这一段
		// default_base_moof 让样本位置从各自的 moof 算起，切开文件、去掉填充数据之后才对得上
		// skip_trailer 不写末尾的 mfra：它记着各分片在整个流里的位置和时间，切开文件、去掉填充数据、时间戳归零之后都对不上
		arguments.AddRange([@"-f", @"mp4", @"-movflags", @"frag_keyframe+empty_moov+default_base_moof+skip_trailer", @"-frag_duration", @"1000000", @"pipe:1"]);

		return arguments;
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
