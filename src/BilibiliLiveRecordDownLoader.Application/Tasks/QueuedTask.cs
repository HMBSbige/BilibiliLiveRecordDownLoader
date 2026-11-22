namespace BilibiliLiveRecordDownLoader.Application.Tasks;

internal readonly record struct QueuedTask(Guid Id, Task<BackgroundTaskSnapshot> Completion);
