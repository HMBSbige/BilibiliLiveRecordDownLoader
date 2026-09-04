namespace MediaToolkit.FFmpeg;

public readonly record struct FFmpegProgress(TimeSpan? Duration, TimeSpan? Position, string Message);
