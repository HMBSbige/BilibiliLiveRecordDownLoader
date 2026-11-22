namespace MediaToolkit.FFmpeg;

public sealed class FFmpegException(int exitCode, string? lastMessage) : Exception($"FFmpeg exited with code {exitCode}: {lastMessage}")
{
	public int ExitCode { get; } = exitCode;

	// 已去掉地址里的查询串，可以直接展示
	public string? LastMessage { get; } = lastMessage;
}
