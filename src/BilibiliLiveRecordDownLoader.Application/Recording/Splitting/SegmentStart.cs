namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// Offset 按网络端交给缓冲的字节计
internal readonly record struct SegmentStart(long Offset, DateTimeOffset StartTime, SplitReason Reason, int Segment);
