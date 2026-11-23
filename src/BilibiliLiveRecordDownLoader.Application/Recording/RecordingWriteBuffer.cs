using System.IO.Pipelines;
using System.Runtime.ExceptionServices;

namespace BilibiliLiveRecordDownLoader.Application.Recording;

// 网络盘短暂卡住（SMB 抖动、NAS 唤醒磁盘）时照常收流；本地盘写得快，缓冲始终很小
internal static class RecordingWriteBuffer
{
	private const long PauseThreshold = 64L << 20;

	public static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(15);

	private static readonly PipeOptions Options = new(pauseWriterThreshold: PauseThreshold, resumeWriterThreshold: PauseThreshold / 2, minimumSegmentSize: 64 << 10, useSynchronizationContext: false);

	// 进度按收到的字节计，反映的是网络；缓冲写满后会话停在写入上，这时被停下算作写盘失败
	// 文件由写盘端关闭，磁盘卡住时调用方不必等在关闭文件上
	public static async Task<StreamEndReason> CopyAsync(LiveStreamSession session, RecordingOutput output, IProgress<long> progress, TimeSpan drainTimeout, CancellationToken cancellationToken)
	{
		Pipe pipe = new(Options);
		StallTrackingWriter writer = new(pipe.Writer);

		// 排空超时后写盘端还在后台用这两个令牌源，要等它结束才能释放
		CancellationTokenSource receiving = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		CancellationTokenSource draining = new();
		bool isDisposalDeferred = false;

		try
		{
			Task writing = WriteToDiskAsync(pipe.Reader, output, receiving, draining.Token);
			StreamEndReason reason = default;
			ExceptionDispatchInfo? receiveError = null;

			try
			{
				reason = await output.ReceiveAsync(session, writer, progress, receiving.Token);
			}
			catch (Exception exception)
			{
				receiveError = ExceptionDispatchInfo.Capture(exception);
			}

			// 先把已收到的数据写完，但磁盘一直不动也不能让停止卡住
			await pipe.Writer.CompleteAsync();

			try
			{
				await writing.WaitAsync(drainTimeout, CancellationToken.None);
			}
			catch (TimeoutException exception)
			{
				// 先取消再抛出，写盘端回来时才一定看得到尝试已经结束
				await draining.CancelAsync();
				isDisposalDeferred = true;
				_ = writing.ContinueWith(task =>
				{
					Dispose(receiving, draining);
					return task.Exception;
				}, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
				throw new RecordingWriteException($"Writing the buffered recording data did not finish within {drainTimeout}.", AppLanguage.Current.RecordingWriteDrainTimedOut.ToString(drainTimeout.TotalSeconds.ToString("0.#", CultureInfo.CurrentCulture)), exception);
			}

			if (writer.IsStalled && receiveError is { SourceException: var stopped })
			{
				throw new RecordingWriteException($"The recording was stopped while {PauseThreshold >> 20} MiB of received data was waiting for the disk.", AppLanguage.Current.RecordingWriteStalledOnStop.ToString((PauseThreshold >> 20).ToString(CultureInfo.CurrentCulture)), stopped);
			}

			receiveError?.Throw();
			return reason;
		}
		finally
		{
			if (!isDisposalDeferred)
			{
				Dispose(receiving, draining);
			}
		}
	}

	private static void Dispose(CancellationTokenSource receiving, CancellationTokenSource draining)
	{
		receiving.Dispose();
		draining.Dispose();
	}

	private static async Task WriteToDiskAsync(PipeReader reader, RecordingOutput output, CancellationTokenSource receiving, CancellationToken cancellationToken)
	{
		try
		{
			await output.WriteAsync(reader, cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			await output.AbandonAsync();
			throw;
		}
		catch (Exception exception)
		{
			// 立即停止收流，让写入错误浮出；取消时回调出错也要关掉文件
			try
			{
				await receiving.CancelAsync();
			}
			finally
			{
				await output.AbandonAsync();
			}

			if (exception is RecordingWriteException)
			{
				throw;
			}

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

		// 等待缓冲腾出空间时被取消，标记会一直保留
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
}
