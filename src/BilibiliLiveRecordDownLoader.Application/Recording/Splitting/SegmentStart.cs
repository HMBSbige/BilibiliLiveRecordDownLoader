namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// 网络端交给缓冲的第 Offset 个字节起属于新的一段；StartTime 是这段第一个单元到达的时间，新文件以它命名
internal readonly record struct SegmentStart(long Offset, DateTimeOffset StartTime, SplitReason Reason);
