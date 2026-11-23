using BilibiliLiveRecordDownLoader.Application.Contracts.Tasks;

namespace BilibiliLiveRecordDownLoader.Application.Contracts.Media;

public interface IMediaToolAppService
{
	Task<string?> GetFFmpegVersionAsync(CancellationToken cancellationToken = default);

	// 外层任务在加入队列后完成，内层任务在转封装结束后给出最终状态
	Task<Task<BackgroundTaskSnapshot>> QueueRemuxAsync(RemuxInput input, CancellationToken cancellationToken = default);

	Task QueueCutAsync(CutInput input, CancellationToken cancellationToken = default);

	// source 不能转封装时为 null
	string? SuggestRemuxDestination(string source);

	string? SuggestCutDestination(string source);
}
