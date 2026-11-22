namespace BilibiliLiveRecordDownLoader.Application.Contracts.Media;

public sealed record RemuxInput(string Source, string Destination, bool DeleteSource, bool RepairFlv)
{
	// FFmpeg 的 ffmetadata 章节文件，转封装时写进输出；修复 FLV 时不支持
	public string? Chapters { get; init; }
}
