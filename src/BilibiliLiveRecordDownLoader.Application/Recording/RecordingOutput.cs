using System.IO.Pipelines;

namespace BilibiliLiveRecordDownLoader.Application.Recording;

// 写缓冲两端看到的录制输出：网络端把会话的数据写进缓冲，写盘端把缓冲里的数据落到文件
internal abstract class RecordingOutput
{
	public abstract Task<StreamEndReason> ReceiveAsync(LiveStreamSession session, PipeWriter writer, IProgress<long> progress, CancellationToken cancellationToken);

	// 写完后关闭文件，文件流自己缓冲的数据关闭时才写出
	public abstract Task WriteAsync(PipeReader reader, CancellationToken cancellationToken);

	// 写不进去时丢下还开着的文件
	public abstract ValueTask AbandonAsync();

	// 写不进去的文件关闭时还会再写一次自己的缓冲，结果已经由最初的错误说明
	protected static async ValueTask AbandonAsync(Stream file)
	{
		try
		{
			await file.DisposeAsync();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			// 已经报告过写盘失败
		}
	}
}

internal sealed class SingleFileOutput(Stream file) : RecordingOutput
{
	public override Task<StreamEndReason> ReceiveAsync(LiveStreamSession session, PipeWriter writer, IProgress<long> progress, CancellationToken cancellationToken)
	{
		return session.CopyToAsync(writer.AsStream(), progress, cancellationToken);
	}

	public override async Task WriteAsync(PipeReader reader, CancellationToken cancellationToken)
	{
		await reader.CopyToAsync(file, cancellationToken);
		await file.DisposeAsync();
	}

	public override ValueTask AbandonAsync()
	{
		return AbandonAsync(file);
	}
}
