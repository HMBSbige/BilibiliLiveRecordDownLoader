namespace BilibiliLiveRecordDownLoader.Application.Contracts.Media;

public closed record RemuxMode
{
	public sealed record Plain : RemuxMode;

	// 源文件不是 FLV 时按 Plain 转封装
	public sealed record RepairFlv : RemuxMode;

	// FFmpeg 的 ffmetadata 章节文件，转封装时写进输出
	public sealed record WithChapters(string Path) : RemuxMode;
}
