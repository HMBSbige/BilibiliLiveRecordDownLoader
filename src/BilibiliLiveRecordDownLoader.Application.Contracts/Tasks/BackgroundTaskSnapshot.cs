namespace BilibiliLiveRecordDownLoader.Application.Contracts.Tasks;

public sealed record BackgroundTaskSnapshot(Guid Id, string Title, BackgroundTaskStatus Status, double? Progress, string? Message);
