using System.Buffers;

namespace BilibiliLiveRecordDownLoader.Application.Recording;

internal sealed class FFmpegStreamSession(FFmpegRunner ffmpeg, IReadOnlyList<string> arguments, Uri source) : LiveStreamSession
{
	private const int BufferSize = 64 * 1024;

	public override Uri Source { get; } = source;

	// 和 FFmpegRunner 等 FFmpeg 退出的时限同一量级；那个时限在媒体工具库内部，这里拿不到
	internal TimeSpan FlushTimeout { get; init; } = TimeSpan.FromSeconds(5);

	public override async Task<StreamEndReason> CopyToAsync(Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
	{
		using CancellationTokenSource writing = new();
		// ReSharper disable once AccessToDisposedClosure
		await using CancellationTokenRegistration stopping = cancellationToken.Register(() => writing.CancelAfter(FlushTimeout));
		await ffmpeg.RunAsync(arguments, output => CopyAsync(output, destination, progress, writing.Token), null, cancellationToken);
		return StreamEndReason.EndOfStream;
	}

	public override ValueTask DisposeAsync()
	{
		return ValueTask.CompletedTask;
	}

	// 读取不取消：FFmpeg 收到 q 后还会冲出剩余数据，要读到它退出，FFmpegRunner 保证它最终会退出
	// 写入要能取消：缓冲写满时写入不返回，FFmpegRunner 就等不到读取结束
	private static async Task CopyAsync(Stream output, Stream destination, IProgress<long>? progress, CancellationToken writing)
	{
		byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

		try
		{
			while (await output.ReadAsync(buffer, CancellationToken.None) is var read and > 0)
			{
				await destination.WriteAsync(buffer.AsMemory(0, read), writing);
				progress?.Report(read);
			}
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}
}
