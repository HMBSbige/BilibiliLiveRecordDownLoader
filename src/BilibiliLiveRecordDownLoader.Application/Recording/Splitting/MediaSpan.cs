namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

internal readonly record struct MediaSpan(TimeSpan Start, TimeSpan End)
{
	// 音视频交错一般只差几百毫秒，倒退超过这个值视为源时间线重新开始，比如主播重新推流
	public const long TimelineRestartToleranceMilliseconds = 1000;

	public static readonly TimeSpan TimelineRestartTolerance = TimeSpan.FromMilliseconds(TimelineRestartToleranceMilliseconds);

	public static MediaSpan At(TimeSpan time)
	{
		return new MediaSpan(time, time);
	}
}

// End 是元素结束处在收到的字节里的位置
internal readonly record struct MediaMark(long End, MediaSpan Span, VideoResolution? Resolution);
