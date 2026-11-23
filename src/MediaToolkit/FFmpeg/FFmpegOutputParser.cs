using System.Globalization;
using System.Text.RegularExpressions;

namespace MediaToolkit.FFmpeg;

internal static partial class FFmpegOutputParser
{
	// 章节标题和元数据会原样打印出来，只能认 ASCII 数字：\d 还匹配全角数字，解析时会抛异常；小时最多 8 位，换算成 tick 不会溢出
	private const string TimePattern = @"(?<negative>-)?(?<hours>[0-9]{1,8}):(?<minutes>[0-9]{2}):(?<seconds>[0-9]{2})(?:\.(?<fraction>[0-9]+))?";

	[GeneratedRegex(@"^ffmpeg version (?<version>\S+)")]
	private static partial Regex VersionRegex();

	[GeneratedRegex(@"Duration:\s*" + TimePattern)]
	private static partial Regex DurationRegex();

	[GeneratedRegex(@"time=\s*" + TimePattern)]
	private static partial Regex PositionRegex();

	// HLS 的 Opening '<url>' 用单引号括住地址，打开失败时地址后面紧跟句号
	[GeneratedRegex(@"(?<url>[A-Za-z][A-Za-z0-9+.\-]*://[^\s'""?]*)\?[^\s'""]*?(?=[.,:;]?(?:[\s'""]|$))")]
	private static partial Regex UrlQueryRegex();

	public static string? ParseVersion(string output)
	{
		return VersionRegex().Match(output) is { Success: true } match ? match.Groups[@"version"].Value : null;
	}

	// 直播地址的查询串里有 uid 和签名
	public static string RedactUrlQueries(string line)
	{
		return UrlQueryRegex().Replace(line, @"${url}?…");
	}

	// 统计行可能是 time=N/A，只能按开头判断
	public static bool IsDiagnostic(string line)
	{
		ReadOnlySpan<char> trimmed = line.AsSpan().TrimStart();

		return !trimmed.StartsWith(@"frame=", StringComparison.Ordinal)
			&& !trimmed.StartsWith(@"size=", StringComparison.Ordinal)
			&& !trimmed.StartsWith(@"Conversion failed!", StringComparison.Ordinal)
			&& !trimmed.StartsWith(@"Exiting normally, received signal", StringComparison.Ordinal);
	}

	// Duration 只在开头列出输入信息时打印
	public static FFmpegProgress ParseProgress(string line, FFmpegProgress previous)
	{
		return new FFmpegProgress(ParseTime(DurationRegex().Match(line)) ?? previous.Duration, ParseTime(PositionRegex().Match(line)) ?? previous.Position, line);
	}

	private static TimeSpan? ParseTime(Match match)
	{
		if (!match.Success)
		{
			return null;
		}

		long ticks = long.Parse(match.Groups[@"hours"].ValueSpan, CultureInfo.InvariantCulture) * TimeSpan.TicksPerHour
			+ int.Parse(match.Groups[@"minutes"].ValueSpan, CultureInfo.InvariantCulture) * TimeSpan.TicksPerMinute
			+ int.Parse(match.Groups[@"seconds"].ValueSpan, CultureInfo.InvariantCulture) * TimeSpan.TicksPerSecond
			+ ParseFractionTicks(match.Groups[@"fraction"].ValueSpan);

		return TimeSpan.FromTicks(match.Groups[@"negative"].Success ? -ticks : ticks);
	}

	private static long ParseFractionTicks(ReadOnlySpan<char> fraction)
	{
		Span<char> digits = stackalloc char[7];
		digits.Fill('0');
		fraction.Slice(0, Math.Min(fraction.Length, digits.Length)).CopyTo(digits);

		return long.Parse(digits, CultureInfo.InvariantCulture);
	}
}
