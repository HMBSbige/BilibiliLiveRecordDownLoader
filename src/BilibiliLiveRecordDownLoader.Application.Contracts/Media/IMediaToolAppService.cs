using BilibiliLiveRecordDownLoader.Application.Contracts.Tasks;

namespace BilibiliLiveRecordDownLoader.Application.Contracts.Media;

public interface IMediaToolAppService
{
	Task<string?> GetFFmpegVersionAsync(CancellationToken cancellationToken = default);

	// 加入队列后即返回，得到的任务在转封装结束后给出最终状态
	Task<Task<BackgroundTaskSnapshot>> QueueRemuxAsync(RemuxInput input, CancellationToken cancellationToken = default);

	Task QueueCutAsync(CutInput input, CancellationToken cancellationToken = default);

	// 建议的输出跳过已有的文件和排队中任务的输出；source 不能转封装时为 null
	string? SuggestRemuxDestination(string source);

	// 建议的输出跳过已有的文件和排队中任务的输出；source 为空时为 null
	string? SuggestCutDestination(string source);
}
