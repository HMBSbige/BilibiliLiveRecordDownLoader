namespace MediaToolkit.Flv;

public sealed record FlvExtractResult(string? VideoPath, string? AudioPath, double? VideoFrameRate = null);
