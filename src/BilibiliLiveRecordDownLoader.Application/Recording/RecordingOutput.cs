using System.IO.Pipelines;

namespace BilibiliLiveRecordDownLoader.Application.Recording;

internal abstract class RecordingOutput
{
	public abstract Task<StreamEndReason> ReceiveAsync(LiveStreamSession session, PipeWriter writer, IProgress<long> progress, CancellationToken cancellationToken);

	// 返回前关闭文件，文件流自己缓冲的数据关闭时才写出
	public abstract Task WriteAsync(PipeReader reader, CancellationToken cancellationToken);

	public abstract ValueTask AbandonAsync();

	protected static async ValueTask AbandonAsync(Stream file)
	{
		try
		{
			await file.DisposeAsync();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
		}
	}
}
