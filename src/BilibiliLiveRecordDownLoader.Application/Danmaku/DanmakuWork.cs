namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

internal sealed record DanmakuRun(Func<DanmakuFileHeader> CreateHeader, Func<CancellationToken, ValueTask<DanmakuRunSettings>> GetSettingsAsync);

internal sealed record DanmakuRunSettings(DanmakuProfile Profile, string RecordDirectory, bool IncludeTitleInFileName, bool SplitOnTitleChange);

internal sealed record DanmakuSegment(string Path, VideoResolution? Resolution, TimeSpan? Duration);

internal closed record DanmakuWork
{
	public sealed record Received(DanmuMessage Message) : DanmakuWork;

	public sealed record Disconnected(DanmuReceiveTime Time) : DanmakuWork;

	public sealed record TitleChanged(string Title, long Timestamp) : DanmakuWork;

	public sealed record VideoStarted(IRecordingTimeline Timeline, string Path, DanmakuRun Run, long Timestamp) : DanmakuWork;

	public sealed record SegmentStarted(string Path, long Timestamp) : DanmakuWork;

	// 处理到这里时，接收时刻落在这一段的弹幕都已经排在前面
	public sealed record SegmentCompleted(string Path, TaskCompletionSource<DanmakuSegment?> Completion) : DanmakuWork;

	public sealed record VideoEnded : DanmakuWork;

	public sealed record TextOnlyStarted(DanmakuRun Run, long Timestamp) : DanmakuWork;

	public sealed record TextOnlyStopped : DanmakuWork;

	public sealed record SettingsChanged : DanmakuWork;

	public sealed record SessionEnded : DanmakuWork;
}
