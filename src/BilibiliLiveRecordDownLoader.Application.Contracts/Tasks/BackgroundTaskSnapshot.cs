namespace BilibiliLiveRecordDownLoader.Application.Contracts.Tasks;

// Speed 与 FFmpeg 输出的 speed 含义相同
public sealed record BackgroundTaskSnapshot
(
	Guid Id,
	string Title,
	string? SourcePath,
	string? TargetPath,
	BackgroundTaskStatus Status,
	double? Progress,
	double? Speed,
	string? Message
)
{
	public bool IsActive => Status is BackgroundTaskStatus.Queued or BackgroundTaskStatus.Running;
}
