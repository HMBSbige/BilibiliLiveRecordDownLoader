namespace BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

// 一个元素在源时间轴上从第一帧到最后一帧的媒体时间；FLV tag 和 PES 只有一个时间，起止相同
internal readonly record struct MediaSpan(TimeSpan Start, TimeSpan End)
{
	public static MediaSpan At(TimeSpan time)
	{
		return new MediaSpan(time, time);
	}
}

// 收到的第 End 个字节为止的元素带着媒体时间；Resolution 是这个元素到达时流头里的画面尺寸
internal readonly record struct MediaMark(long End, MediaSpan Span, VideoResolution? Resolution);
