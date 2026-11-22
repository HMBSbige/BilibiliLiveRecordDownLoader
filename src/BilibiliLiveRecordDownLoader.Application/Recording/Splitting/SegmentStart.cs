namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// Offset 按网络端交给缓冲的字节计；StartTime 是这段第一个单元到达的时间，用来给新文件命名
internal readonly record struct SegmentStart(long Offset, DateTimeOffset StartTime, SplitReason Reason, int Segment);
