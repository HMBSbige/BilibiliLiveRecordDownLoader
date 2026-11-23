using BilibiliLive.Streams;
using BilibiliLiveRecordDownLoader.Application.Recording;
using System.IO.Pipelines;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class RecordingWriteBufferTest
{
	private const int ChunkSize = 64 * 1024;

	private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(5);

	[Test]
	[Timeout(10_000)]
	public async Task NetworkKeepsReceivingWhileTheDiskStallsThenEverythingIsWrittenAsync(CancellationToken cancellationToken)
	{
		ChunkSession session = new(10, false);
		GatedStream output = new();
		Progress progress = new();

		Task<StreamEndReason> copy = CopyAsync(session, output, progress, DrainTimeout, cancellationToken);
		await progress.WaitForAsync(session.Length, cancellationToken);
		(int WrittenLength, bool IsFinished) whileStalled = (output.Written.Length, copy.IsCompleted);
		output.Open();
		StreamEndReason reason = await copy;
		byte[] written = output.Written;

		using (Assert.Multiple())
		{
			await Assert.That(whileStalled).IsEqualTo((0, false));
			await Assert.That(reason).IsEqualTo(StreamEndReason.EndOfStream);
			await Assert.That((written.Length, written.AsSpan().CommonPrefixLength(session.Data))).IsEqualTo((session.Data.Length, session.Data.Length));
		}
	}

	[Test]
	[Timeout(10_000)]
	public async Task StoppingDrainsWhatWasAlreadyReceivedAsync(CancellationToken cancellationToken)
	{
		ChunkSession session = new(3, true);
		GatedStream output = new();
		Progress progress = new();
		using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

		Task<StreamEndReason> copy = CopyAsync(session, output, progress, DrainTimeout, stop.Token);
		await progress.WaitForAsync(session.Length, cancellationToken);
		await stop.CancelAsync();
		output.Open();

		await Assert.ThrowsAsync<OperationCanceledException>(() => copy);
		byte[] written = output.Written;
		await Assert.That((written.Length, written.AsSpan().CommonPrefixLength(session.Data))).IsEqualTo((session.Data.Length, session.Data.Length));
	}

	[Test]
	[Timeout(10_000)]
	public async Task StoppingWhileTheBufferIsFullIsAWriteFailureEvenIfTheDiskRecoversAsync(CancellationToken cancellationToken)
	{
		ChunkSession session = new(int.MaxValue, false);
		GatedStream output = new();
		using CancellationTokenSource idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

		Task<StreamEndReason> copy = CopyAsync(session, output, new Progress(), DrainTimeout, idle.Token);
		await session.Stalled.WaitAsync(cancellationToken);
		await idle.CancelAsync();
		output.Open();

		await Assert.ThrowsExactlyAsync<RecordingWriteException>(() => copy);
	}

	[Test]
	[Timeout(10_000)]
	public async Task WriteFailureStopsReceivingAndSurfacesAsync(CancellationToken cancellationToken)
	{
		ChunkSession session = new(int.MaxValue, false);
		GatedStream output = new(new IOException("disk full"), new IOException("still full"));
		output.Open();

		await Assert.That(() => CopyAsync(session, output, new Progress(), DrainTimeout, cancellationToken)).Throws<RecordingWriteException>().WithInnerException().WithMessage("disk full");
		await Assert.That(session.WasCancelled).IsTrue();
	}

	[Test]
	[Timeout(10_000)]
	public async Task FailureToFlushTheFileOnCloseIsAWriteFailureAsync(CancellationToken cancellationToken)
	{
		ChunkSession session = new(3, false);
		GatedStream output = new(closeFailure: new IOException("disk full"));
		output.Open();

		await Assert.That(() => CopyAsync(session, output, new Progress(), DrainTimeout, cancellationToken)).Throws<RecordingWriteException>().WithInnerException().WithMessage("disk full");
	}

	[Test]
	[Timeout(10_000)]
	public async Task NetworkFailureIsNotReportedAsAWriteFailureAsync(CancellationToken cancellationToken)
	{
		ChunkSession session = new(3, false, new IOException("connection reset"));
		GatedStream output = new();
		output.Open();

		await Assert.That(() => CopyAsync(session, output, new Progress(), DrainTimeout, cancellationToken)).ThrowsExactly<IOException>().WithMessage("connection reset");
		byte[] written = output.Written;
		await Assert.That((written.Length, written.AsSpan().CommonPrefixLength(session.Data))).IsEqualTo((session.Data.Length, session.Data.Length));
	}

	[Test]
	[Timeout(10_000)]
	public async Task DiskThatNeverRecoversCannotHangTheStopAsync(CancellationToken cancellationToken)
	{
		ChunkSession session = new(1, true);
		GatedStream output = new();
		Progress progress = new();
		using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

		Task<StreamEndReason> copy = CopyAsync(session, output, progress, TimeSpan.FromMilliseconds(100), stop.Token);
		await progress.WaitForAsync(session.Length, cancellationToken);
		await stop.CancelAsync();

		await Assert.ThrowsExactlyAsync<RecordingWriteException>(() => copy);
		output.Open();
	}

	// 网络盘断开时卡住的写入常常等到会话超时才以 IO 错误结束，文件仍要关掉，后处理才删得了它
	[Test]
	[Timeout(10_000)]
	public async Task WriteFailingAfterTheDrainTimeoutStillClosesTheFileAsync(CancellationToken cancellationToken)
	{
		ChunkSession session = new(1, true);
		GatedStream output = new(new IOException("the network name is no longer available"));
		Progress progress = new();
		using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

		Task<StreamEndReason> copy = CopyAsync(session, output, progress, TimeSpan.FromMilliseconds(100), stop.Token);
		await progress.WaitForAsync(session.Length, cancellationToken);
		await stop.CancelAsync();
		await Assert.ThrowsExactlyAsync<RecordingWriteException>(() => copy);
		output.Open();

		await Assert.That(output.Closed.WaitAsync(TestWait.DefaultTimeout, cancellationToken)).ThrowsNothing();
	}

	private static Task<StreamEndReason> CopyAsync(LiveStreamSession session, Stream file, IProgress<long> progress, TimeSpan drainTimeout, CancellationToken cancellationToken)
	{
		return RecordingWriteBuffer.CopyAsync(session, new SingleFileOutput(file), progress, drainTimeout, cancellationToken);
	}

	private sealed class SingleFileOutput(Stream file) : RecordingOutput
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

	private sealed class ChunkSession(int chunks, bool waitForCancellation, Exception? failure = null) : LiveStreamSession
	{
		private readonly TaskCompletionSource _stalled = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public override Uri Source { get; } = new("https://cdn.test/live.flv");

		public Task Stalled => _stalled.Task;

		public byte[] Data { get; } = [.. Enumerable.Range(0, Math.Min(chunks, 16) * ChunkSize).Select(static i => (byte)(i * 31))];

		public long Length => Data.Length;

		public bool WasCancelled { get; private set; }

		public override async Task<StreamEndReason> CopyToAsync(Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
		{
			try
			{
				for (int chunk = 0; chunk < chunks; ++chunk)
				{
					// 真实的会话在读网络时响应取消
					await Task.Yield();
					cancellationToken.ThrowIfCancellationRequested();

					ValueTask write = destination.WriteAsync(Data.AsMemory(chunk % 16 * ChunkSize, ChunkSize), cancellationToken);

					if (!write.IsCompleted)
					{
						_stalled.TrySetResult();
					}

					await write;
					progress?.Report(ChunkSize);
				}

				if (failure is not null)
				{
					throw failure;
				}

				if (waitForCancellation)
				{
					await Task.Delay(Timeout.Infinite, cancellationToken);
				}

				return StreamEndReason.EndOfStream;
			}
			catch (OperationCanceledException)
			{
				WasCancelled = true;
				throw;
			}
		}

		public override ValueTask DisposeAsync()
		{
			return ValueTask.CompletedTask;
		}
	}

	// 写入不理会取消，模拟一个不再响应的网络共享
	private sealed class GatedStream(Exception? failure = null, Exception? closeFailure = null) : Stream
	{
		private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly MemoryStream _written = new();

		public Task Closed => _closed.Task;

		public byte[] Written
		{
			get
			{
				lock (_written)
				{
					return _written.ToArray();
				}
			}
		}

		public override bool CanRead => false;

		public override bool CanSeek => false;

		public override bool CanWrite => true;

		public override long Length => throw new NotSupportedException();

		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

		public void Open()
		{
			_gate.TrySetResult();
		}

		public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
		{
			await _gate.Task;

			if (failure is not null)
			{
				throw failure;
			}

			lock (_written)
			{
				_written.Write(buffer.Span);
			}
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException();
		}

		public override void Flush()
		{
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException();
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			throw new NotSupportedException();
		}

		public override void SetLength(long value)
		{
			throw new NotSupportedException();
		}

		// 和文件流一样，关闭时才写出自己缓冲的数据
		public override ValueTask DisposeAsync()
		{
			_closed.TrySetResult();
			return closeFailure is null ? base.DisposeAsync() : ValueTask.FromException(closeFailure);
		}
	}

	private sealed class Progress : IProgress<long>
	{
		private long _total;

		public void Report(long value)
		{
			Interlocked.Add(ref _total, value);
		}

		public Task WaitForAsync(long total, CancellationToken cancellationToken)
		{
			return TestWait.UntilAsync(() => Interlocked.Read(ref _total) >= total, $"{total} bytes are reported", cancellationToken);
		}
	}
}
