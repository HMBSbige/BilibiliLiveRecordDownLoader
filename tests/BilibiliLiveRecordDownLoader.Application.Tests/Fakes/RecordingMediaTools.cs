using BilibiliLiveRecordDownLoader.Application.Contracts.Media;
using System.Collections.Concurrent;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

// 记下排队的转封装，每个都立即成功
internal sealed class RecordingMediaTools : IMediaToolAppService
{
	public ConcurrentQueue<RemuxInput> Remuxes { get; } = new();

	public Task<string?> GetFFmpegVersionAsync(CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}

	public Task<Task<BackgroundTaskSnapshot>> QueueRemuxAsync(RemuxInput input, CancellationToken cancellationToken = default)
	{
		Remuxes.Enqueue(input);
		return Task.FromResult(Task.FromResult(new BackgroundTaskSnapshot(Guid.NewGuid(), "remux", input.Source, input.Destination, BackgroundTaskStatus.Completed, 1, null, null)));
	}

	public Task QueueCutAsync(CutInput input, CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}
}
