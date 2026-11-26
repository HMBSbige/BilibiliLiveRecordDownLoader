using System.Diagnostics.CodeAnalysis;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

internal sealed class FakeDisk
{
	private readonly Lock _lock = new();
	private TaskCompletionSource? _stall;
	private bool _isFull;

	public void Fill()
	{
		Release(isFull: true);
	}

	public void Free()
	{
		Release(isFull: false);
	}

	// 卡住的写入等到 Fill 或 Free 才失败或写完，就像网络盘断开后过一阵才报错或恢复
	public void Stall()
	{
		lock (_lock)
		{
			_stall ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		}
	}

	[SuppressMessage("Style", "IDE0060", Justification = "签名要对上 LiveRecorder.CreateFile")]
	public Stream CreateFile(string path)
	{
		return new FakeFile(WriteAsync);
	}

	private void Release(bool isFull)
	{
		TaskCompletionSource? stall;

		lock (_lock)
		{
			_isFull = isFull;
			stall = _stall;
			_stall = null;
		}

		stall?.TrySetResult();
	}

	private async ValueTask WriteAsync(CancellationToken cancellationToken)
	{
		Task? stall;

		lock (_lock)
		{
			stall = _stall?.Task;
		}

		if (stall is not null)
		{
			await stall.WaitAsync(cancellationToken);
		}

		lock (_lock)
		{
			if (_isFull)
			{
				throw new IOException("disk full");
			}
		}
	}
}

internal sealed class FakeFile(Func<CancellationToken, ValueTask> write) : Stream
{
	public override bool CanRead => false;

	public override bool CanSeek => false;

	public override bool CanWrite => true;

	public override long Length => throw new NotSupportedException();

	public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

	public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
	{
		return write(cancellationToken);
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
