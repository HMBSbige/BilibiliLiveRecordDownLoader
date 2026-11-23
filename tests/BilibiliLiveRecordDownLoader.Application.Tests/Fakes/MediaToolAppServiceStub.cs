using BilibiliLiveRecordDownLoader.Application.Contracts.Media;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

internal abstract class MediaToolAppServiceStub : IMediaToolAppService
{
	public virtual Task<string?> GetFFmpegVersionAsync(CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}

	public virtual Task<Task<BackgroundTaskSnapshot>> QueueRemuxAsync(RemuxInput input, CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}

	public virtual Task QueueCutAsync(CutInput input, CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}

	public virtual string? SuggestRemuxDestination(string source)
	{
		throw new NotSupportedException();
	}

	public virtual string? SuggestCutDestination(string source)
	{
		throw new NotSupportedException();
	}
}
