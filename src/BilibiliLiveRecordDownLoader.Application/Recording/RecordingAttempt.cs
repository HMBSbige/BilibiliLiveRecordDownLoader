namespace BilibiliLiveRecordDownLoader.Application.Recording;

internal sealed record RecordingAttempt(LiveStreamCandidate Candidate, Uri Endpoint, TimeSpan ActiveDuration, RecordingEnd End)
{
	// 远长于 FLV 关键帧间隔和 HLS 分片时长；持续送过这么久数据之后的断流按 CDN 偶发故障处理，回到首选流
	public static readonly TimeSpan QuickFailureThreshold = TimeSpan.FromSeconds(30);

	// ActiveDuration 只计到最后一次收到数据；HLS 换时间线是正常分段，不是线路故障
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

	public string DisplayText => this switch
	{
		Completed { Reason: StreamEndReason.Discontinuity } => AppLanguage.Current.RecordingStreamRestarted,
		Completed => AppLanguage.Current.RecordingStreamEnded,
		Idle idle => AppLanguage.Current.RecordingStreamIdle.ToString(idle.Timeout.TotalSeconds.ToString("0.#", CultureInfo.CurrentCulture)),
		Faulted faulted => GetDisplayText(faulted.Exception),
		WriteFailed writeFailed => writeFailed.Exception.DisplayText
	};

	// FFmpeg 的最后几行输出是唯一的诊断信息，要保留
	public static string GetDisplayText(Exception exception)
	{
		return exception switch
		{
			TimeoutException => AppLanguage.Current.RecordingConnectionTimedOut,
			FFmpegException ffmpeg => AppLanguage.Current.FFmpegFailed.ToString(ffmpeg.ExitCode, ffmpeg.LastMessage ?? string.Empty),
			_ => exception.Message
		};
	}
}
