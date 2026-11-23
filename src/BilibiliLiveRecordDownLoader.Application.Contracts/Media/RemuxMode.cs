namespace BilibiliLiveRecordDownLoader.Application.Contracts.Media;

public closed record RemuxMode
{
	public sealed record Plain : RemuxMode;

	public sealed record RepairFlv : RemuxMode;

	// Path 是 FFmpeg 的 ffmetadata 章节文件
	public sealed record WithChapters(string Path) : RemuxMode;
}
