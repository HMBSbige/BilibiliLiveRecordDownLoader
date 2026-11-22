namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// 一个元素在源时间轴上从第一帧到最后一帧的媒体时间；FLV tag 和 PES 只有一个时间，起止相同
internal readonly record struct MediaSpan(TimeSpan Start, TimeSpan End)
{
	// 关键帧前后音视频交错一般只差几百毫秒，倒退得比这还多说明源的时间线重新开始了，比如主播重新推流，要从已经到达的时间接着往后排。
	// FlvSplitter 按它改写文件里的时间戳，时间轴按它放弹幕，两边用同一个值才对得上
	public const long TimelineRestartToleranceMilliseconds = 1000;

	public static MediaSpan At(TimeSpan time)
	{
		return new MediaSpan(time, time);
	}
}

// 收到的第 End 个字节为止的元素带着媒体时间；Resolution 是这个元素到达时流头里的画面尺寸
internal readonly record struct MediaMark(long End, MediaSpan Span, VideoResolution? Resolution);
