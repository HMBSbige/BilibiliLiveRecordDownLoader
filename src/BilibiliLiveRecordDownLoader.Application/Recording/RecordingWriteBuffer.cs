using System.IO.Pipelines;
using System.Runtime.ExceptionServices;

namespace BilibiliLiveRecordDownLoader.Application.Recording;

// 网络读取和写盘之间的有界缓冲：网络盘短暂卡住（SMB 抖动、NAS 唤醒磁盘）时照常收流，本地盘写得快，缓冲始终很小
internal static class RecordingWriteBuffer
{
	private const long PauseThreshold = 64L << 20;

	public static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(15);

	private static readonly PipeOptions Options = new(pauseWriterThreshold: PauseThreshold, resumeWriterThreshold: PauseThreshold / 2, minimumSegmentSize: 64 << 10, useSynchronizationContext: false);

	// 进度按收到的字节计，缓冲没满时空闲判断和速度反映的是网络；缓冲写满后会话停在写入上，这时被停下算作写盘失败
	// destination 由写入端关闭，磁盘卡住时调用方不必等在关闭文件上
	public static async Task<StreamEndReason> CopyAsync(LiveStreamSession session, Stream destination, IProgress<long> progress, TimeSpan drainTimeout, CancellationToken cancellationToken)
	{
		Pipe pipe = new(Options);
		StallTrackingWriter writer = new(pipe.Writer);
		using CancellationTokenSource receiving = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		using CancellationTokenSource draining = new();
		Task writing = WriteAsync(pipe.Reader, destination, receiving, draining.Token);
		StreamEndReason reason = default;
		ExceptionDispatchInfo? receiveError = null;

		try
		{
			reason = await session.CopyToAsync(writer.AsStream(), progress, receiving.Token);
		}
		catch (Exception exception)
		{
			receiveError = ExceptionDispatchInfo.Capture(exception);
		}

		// 停止录制、空闲超时和断流时都先把已收到的数据写完，但磁盘一直不动也不能让停止卡住
		await pipe.Writer.CompleteAsync();
		draining.CancelAfter(drainTimeout);

		try
		{
			await writing.WaitAsync(drainTimeout, CancellationToken.None);
		}
		catch (Exception exception) when (exception is TimeoutException || (exception is OperationCanceledException && draining.IsCancellationRequested))
		{
			_ = writing.ContinueWith(static task => task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
			throw new RecordingWriteException($"Writing the buffered recording data did not finish within {drainTimeout}.", exception);
		}

		if (writer.IsStalled && receiveError is { SourceException: var stopped })
		{
			throw new RecordingWriteException($"The recording was stopped while {PauseThreshold >> 20} MiB of received data was waiting for the disk.", stopped);
		}

		receiveError?.Throw();
		return reason;
	}

	private static async Task WriteAsync(PipeReader reader, Stream destination, CancellationTokenSource receiving, CancellationToken cancellationToken)
	{
		try
		{
			await reader.CopyToAsync(destination, cancellationToken);

			// 文件流自己缓冲的数据关闭时才写出，这里的失败同样是写盘失败
			await destination.DisposeAsync();
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			await AbandonAsync(destination);
			throw;
		}
		catch (Exception exception)
		{
			// 写不进去时继续收流只会白白占用带宽，立即停止网络读取并让写入错误浮出
			await receiving.CancelAsync();
			await AbandonAsync(destination);
			throw new RecordingWriteException(exception);
		}
		finally
		{
			await reader.CompleteAsync();
		}
	}

	private sealed class StallTrackingWriter(PipeWriter inner) : PipeWriter
	{
		private volatile bool _isStalled;

		// 写入在等待缓冲腾出空间时被取消，标记会一直保留
		public bool IsStalled => _isStalled;

		public override void Advance(int bytes)
		{
			inner.Advance(bytes);
		}

		public override Memory<byte> GetMemory(int sizeHint = 0)
		{
			return inner.GetMemory(sizeHint);
		}

		public override Span<byte> GetSpan(int sizeHint = 0)
		{
			return inner.GetSpan(sizeHint);
		}

		public override void CancelPendingFlush()
		{
			inner.CancelPendingFlush();
		}

		public override void Complete(Exception? exception = null)
		{
			inner.Complete(exception);
		}

		public override ValueTask CompleteAsync(Exception? exception = null)
		{
			return inner.CompleteAsync(exception);
		}

		public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
		{
			return Track(inner.FlushAsync(cancellationToken));
		}

		public override ValueTask<FlushResult> WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken = default)
		{
			return Track(inner.WriteAsync(source, cancellationToken));
		}

		private ValueTask<FlushResult> Track(ValueTask<FlushResult> flush)
		{
			return flush.IsCompleted ? flush : WaitAsync(flush);
		}

		private async ValueTask<FlushResult> WaitAsync(ValueTask<FlushResult> flush)
		{
			_isStalled = true;
			FlushResult result = await flush;
			_isStalled = false;
			return result;
		}
	}

	// 写不进去的文件关闭时还会再写一次自己的缓冲，结果已经由最初的错误说明
	private static async ValueTask AbandonAsync(Stream destination)
	{
		try
		{
			await destination.DisposeAsync();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			// 已经报告过写盘失败
		}
	}
}
