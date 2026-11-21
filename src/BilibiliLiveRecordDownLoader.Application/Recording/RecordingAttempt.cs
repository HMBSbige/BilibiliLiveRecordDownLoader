namespace BilibiliLiveRecordDownLoader.Application.Recording;

internal sealed record RecordingAttempt(LiveStreamCandidate Candidate, Uri Endpoint, TimeSpan ActiveDuration, RecordingEnd End)
{
	// 远长于 FLV 关键帧间隔和 HLS 分片时长，能持续送这么久数据的线路才算可用；之后的断流按 CDN 偶发故障处理，回到首选流
	public static readonly TimeSpan QuickFailureThreshold = TimeSpan.FromSeconds(30);

	// 只计到最后一次收到数据为止，卡住后等空闲超时的时间不算正常录制；HLS 换时间线是正常分段，不是线路故障
	public bool IsQuickFailure => End is not RecordingEnd.Completed { Reason: StreamEndReason.Discontinuity } && ActiveDuration < QuickFailureThreshold;
}

internal closed record RecordingEnd
{
	public sealed record Completed(StreamEndReason Reason) : RecordingEnd;

	public sealed record Idle(TimeSpan Timeout) : RecordingEnd;

	public sealed record Faulted(Exception Exception) : RecordingEnd;

	public sealed record WriteFailed(RecordingWriteException Exception) : RecordingEnd;

	public string Description => this switch
	{
		Completed { Reason: StreamEndReason.Discontinuity } => "the stream started a new timeline",
		Completed => "the stream ended",
		Idle idle => $"no data received for {idle.Timeout}",
		Faulted faulted => faulted.Exception.Message,
		WriteFailed writeFailed => writeFailed.Exception.Message
	};
}
