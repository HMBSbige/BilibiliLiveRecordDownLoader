namespace BilibiliLiveRecordDownLoader.Application.Contracts.Danmaku;

public interface IDanmakuAppService
{
	// 订阅期间这个房间解析弹幕、SC、礼物和上舰，全部退订且房间不在录弹幕时恢复只读直播状态
	// 房间不存在时抛 KeyNotFoundException；房间移除之后订阅仍可调用，只是不再有新消息，状态停在断开
	IDanmakuFeed Subscribe(long roomId);

	// 录制中的文件也能读；文件被截断时返回出错之前的部分，IsComplete 为 false
	// 文件不存在时抛 FileNotFoundException，不是 XML 时抛 InvalidDataException
	Task<DanmakuFileDto> OpenFileAsync(string path, CancellationToken cancellationToken = default);

	// 同主干有视频时重新生成 ASS；偏移超出 DanmakuDefaults 的范围时取边界值
	// 只能改写本程序写的文件，其他格式抛 InvalidDataException；正在录制的文件或被其他程序独占时抛 IOException，原文件不变
	Task<DanmakuOffsetResult> ChangeOffsetAsync(string path, TimeSpan offset, CancellationToken cancellationToken = default);

	// 找同主干的视频，把高能事件前后各留 DanmakuConsts.HighlightExportMargin 的一段排进剪辑任务，返回输出路径
	// 找不到视频时抛 FileNotFoundException，没有 FFmpeg 时和剪辑一样抛出
	Task<string> QueueHighlightExportAsync(string danmakuPath, DanmakuHighlightDto highlight, CancellationToken cancellationToken = default);
}
