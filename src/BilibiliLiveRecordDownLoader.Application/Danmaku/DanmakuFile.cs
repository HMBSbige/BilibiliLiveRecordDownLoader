namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

// IsComplete 为 false 表示文件被截断或中途出错，Records 是出错前读到的部分
internal sealed record DanmakuFile(DanmakuFileSource Source, IReadOnlyList<DanmakuRecord> Records, bool IsComplete);

internal closed record DanmakuFileSource
{
	public sealed record Native(DanmakuFileHeader Header) : DanmakuFileSource;

	// 其他程序写的文件
	public sealed record Standard : DanmakuFileSource;
}
