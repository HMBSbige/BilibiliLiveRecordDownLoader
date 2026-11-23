using System.Diagnostics.CodeAnalysis;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

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

	[SuppressMessage("Style", "IDE0060", Justification = "签名要对上 LiveRecorder.CreateFile")]
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
