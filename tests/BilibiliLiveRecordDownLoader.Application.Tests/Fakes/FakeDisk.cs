namespace BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

// 写满之前和腾出空间之后一切正常
internal sealed class FakeDisk
{
	private volatile bool _isFull;

	public void Fill()
	{
		_isFull = true;
	}

	public void Free()
	{
		_isFull = false;
	}

	public Stream CreateFile(string path)
	{
		return new FakeFile(_ => _isFull ? ValueTask.FromException(new IOException("disk full")) : ValueTask.CompletedTask);
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
