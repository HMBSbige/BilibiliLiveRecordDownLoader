using BilibiliLiveRecordDownLoader.Application.Contracts.Media;
using System.Collections.Concurrent;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

internal sealed class RecordingMediaTools : IMediaToolAppService
{
	public ConcurrentQueue<RemuxInput> Remuxes { get; } = new();

	// 排队时读出的章节内容，键是转封装的目标路径
	public ConcurrentDictionary<string, string> Chapters { get; } = new();

	public Task<string?> GetFFmpegVersionAsync(CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}

	public async Task<Task<BackgroundTaskSnapshot>> QueueRemuxAsync(RemuxInput input, CancellationToken cancellationToken = default)
	{
		if (input.Mode is RemuxMode.WithChapters { Path: var chapters })
		{
			Chapters[input.Destination] = await File.ReadAllTextAsync(chapters, cancellationToken);
		}

		Remuxes.Enqueue(input);
		return Task.FromResult(new BackgroundTaskSnapshot(Guid.NewGuid(), "remux", input.Source, input.Destination, BackgroundTaskStatus.Completed, 1, null, null));
	}

	public Task QueueCutAsync(CutInput input, CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}
}
