namespace BilibiliLiveRecordDownLoader.Application.Contracts.Tasks;

// Speed 与 FFmpeg 输出的 speed 含义相同，无法计算时为 null
// Message 是失败原因，只在失败时有值
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
