namespace BilibiliLiveRecordDownLoader.Application.Contracts.Media;

public interface IMediaToolAppService
{
	Task<string?> GetFFmpegVersionAsync(CancellationToken cancellationToken = default);

	Task QueueRemuxAsync(RemuxInput input, CancellationToken cancellationToken = default);

	Task QueueCutAsync(CutInput input, CancellationToken cancellationToken = default);
}
