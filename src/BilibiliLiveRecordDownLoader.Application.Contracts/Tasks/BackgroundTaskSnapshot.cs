namespace BilibiliLiveRecordDownLoader.Application.Contracts.Tasks;

// Speed 为已处理的媒体时长与实际耗时之比，与 FFmpeg 输出的 speed 含义相同，无法计算时为 null
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
);
