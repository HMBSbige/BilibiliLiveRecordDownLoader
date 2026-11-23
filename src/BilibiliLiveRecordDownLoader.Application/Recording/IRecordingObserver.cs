namespace BilibiliLiveRecordDownLoader.Application.Recording;

internal interface IRecordingObserver
{
	// path 是第一段；timeline 在 RecordAsync 返回前关闭，之后的接收时刻都不再有位置
	// closed 在写盘端关掉最后一段时完成，磁盘卡到排空超时的话会晚于 RecordAsync 返回
	void OnRecordingStarted(string path, RecordingStream stream, IRecordingTimeline timeline, Task closed);

	// 最后一段不调用；调用时这一段的文件已经关闭，下一段的起点已过去至少一个切分单元，接收时刻早于起点的弹幕都已能放置
	void OnSegmentCompleted(string path);

	// 返回之后时间轴才把弹幕放进这一段
	void OnSegmentStarted(string path);

	void OnSpeedChanged(double bytesPerSecond);
}
