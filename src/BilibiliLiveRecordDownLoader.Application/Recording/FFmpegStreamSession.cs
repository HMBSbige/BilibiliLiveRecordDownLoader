using System.Buffers;

namespace BilibiliLiveRecordDownLoader.Application.Recording;

// FFmpeg 拉流后从标准输出交回，写盘、背压和空闲判断与内置录制走同一条路径
internal sealed class FFmpegStreamSession(FFmpegRunner ffmpeg, IReadOnlyList<string> arguments, Uri source) : LiveStreamSession
{
	private const int BufferSize = 64 * 1024;

	public override Uri Source { get; } = source;

	public override async Task<StreamEndReason> CopyToAsync(Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
	{
		await ffmpeg.RunAsync(arguments, output => CopyAsync(output, destination, progress, cancellationToken), null, cancellationToken);
		return StreamEndReason.EndOfStream;
	}

	public override ValueTask DisposeAsync()
	{
		return ValueTask.CompletedTask;
	}

	private static async Task CopyAsync(Stream output, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
	{
		byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

		try
		{
			while (await output.ReadAsync(buffer, cancellationToken) is var read and > 0)
			{
				await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
				progress?.Report(read);
			}
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}
}
