using System.Globalization;
using System.Text.RegularExpressions;

namespace MediaToolkit.FFmpeg;

internal static partial class FFmpegOutputParser
{
	private const string TimePattern = @"(?<negative>-)?(?<hours>\d+):(?<minutes>\d{2}):(?<seconds>\d{2})(?:\.(?<fraction>\d+))?";

	[GeneratedRegex(@"^ffmpeg version (?<version>\S+)", RegexOptions.CultureInvariant)]
	private static partial Regex VersionRegex();

	[GeneratedRegex(@"Duration:\s*" + TimePattern, RegexOptions.CultureInvariant)]
	private static partial Regex DurationRegex();

	[GeneratedRegex(@"time=\s*" + TimePattern, RegexOptions.CultureInvariant)]
	private static partial Regex PositionRegex();

	public static string? ParseVersion(string output)
	{
		return VersionRegex().Match(output) is { Success: true } match ? match.Groups[@"version"].Value : null;
	}

	// Duration 只在开头打印一次，所以没匹配到的字段沿用上一行的值
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
