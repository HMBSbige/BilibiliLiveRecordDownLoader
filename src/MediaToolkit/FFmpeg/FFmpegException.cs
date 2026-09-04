namespace MediaToolkit.FFmpeg;

public sealed class FFmpegException(int exitCode, string? lastMessage) : Exception($"FFmpeg exited with code {exitCode}: {lastMessage}")
{
	public int ExitCode { get; } = exitCode;
}
