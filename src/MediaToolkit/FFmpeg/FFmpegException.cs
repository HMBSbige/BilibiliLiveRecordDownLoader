using System.Globalization;

namespace MediaToolkit.FFmpeg;

public sealed class FFmpegException(int exitCode, string? lastMessage) : Exception(FormatMessage(exitCode, lastMessage))
{
	public int ExitCode { get; } = exitCode;

	public string? LastMessage { get; } = lastMessage;

	// Windows 上崩溃的退出码是负数，有的区域设置会把负号写成 U+2212
	private static string FormatMessage(int exitCode, string? lastMessage)
	{
		return lastMessage is null
			? string.Create(CultureInfo.InvariantCulture, $"FFmpeg exited with code {exitCode}.")
			: string.Create(CultureInfo.InvariantCulture, $"FFmpeg exited with code {exitCode}: {lastMessage}");
	}
}
