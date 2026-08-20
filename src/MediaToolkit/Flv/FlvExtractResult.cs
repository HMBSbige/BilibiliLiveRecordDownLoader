namespace MediaToolkit.Flv;

// 裸 H.264 不带时间戳，合并时用 VideoFrameRate 还原原始帧率
public sealed record FlvExtractResult(string? VideoPath, string? AudioPath, double? VideoFrameRate = null);
