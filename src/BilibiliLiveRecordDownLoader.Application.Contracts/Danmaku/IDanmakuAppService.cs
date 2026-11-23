namespace BilibiliLiveRecordDownLoader.Application.Contracts.Danmaku;

public interface IDanmakuAppService
{
	// 房间不存在时抛 KeyNotFoundException
	IDanmakuFeed Subscribe(long roomId);

	// 文件被截断时返回出错之前的部分，IsComplete 为 false
	// 文件不存在时抛 FileNotFoundException，不是 XML 时抛 InvalidDataException
	Task<DanmakuFileDto> OpenFileAsync(string path, CancellationToken cancellationToken = default);

	// 不是本程序写的文件抛 InvalidDataException；文件正在录制或被其他程序独占时抛 IOException，原文件不变
	Task<DanmakuOffsetResult> ChangeOffsetAsync(string path, TimeSpan offset, CancellationToken cancellationToken = default);

	// 找不到同主干的视频时抛 FileNotFoundException
	Task<string> QueueHighlightExportAsync(string danmakuPath, DanmakuHighlightDto highlight, CancellationToken cancellationToken = default);
}
