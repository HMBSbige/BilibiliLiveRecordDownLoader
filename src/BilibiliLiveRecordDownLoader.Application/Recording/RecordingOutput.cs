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
			// 关闭时还会再写一次缓冲，失败已由最初的错误报告过
		}
	}
}

// 认不出的容器原样写进一个文件，时间轴没有媒体时间，按单调时钟从文件建好时算
internal sealed class SingleFileOutput(Stream file, RecordingTimeline? timeline) : RecordingOutput
{
	public override async Task<StreamEndReason> ReceiveAsync(LiveStreamSession session, PipeWriter writer, IProgress<long> progress, CancellationToken cancellationToken)
	{
		if (timeline is null)
		{
			return await session.CopyToAsync(writer.AsStream(), progress, cancellationToken);
		}

		try
		{
			return await session.CopyToAsync(new ArrivalStream(writer.AsStream(), timeline), progress, cancellationToken);
		}
		finally
		{
			timeline.EndReceiving();
		}
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

	// 字节原样交给缓冲，只在时间轴上记下每次到达
	private sealed class ArrivalStream(Stream inner, RecordingTimeline timeline) : Stream
	{
		private long _received;

		public override bool CanRead => false;

		public override bool CanSeek => false;

		public override bool CanWrite => true;

		public override long Length => throw new NotSupportedException();

		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

		public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
		{
			_received += buffer.Length;
			timeline.AddUnparsed(_received);
			return inner.WriteAsync(buffer, cancellationToken);
		}

		public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
		{
			return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
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
	}
}
