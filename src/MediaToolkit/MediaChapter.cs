namespace MediaToolkit;

// 不属于 FFmpeg：转封装把它写进 FFmpeg 的元数据文件，fMP4 原地转换不经过 FFmpeg，直接写进 MP4
public sealed record MediaChapter(TimeSpan Start, TimeSpan End, string Title);
