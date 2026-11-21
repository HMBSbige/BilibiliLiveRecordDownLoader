using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class RoomRecordingLoopTest
{
	private static async Task EndImmediatelyAsync(StreamRequest request)
	{
		await request.AcceptAsync();
		await request.EndAsync();
	}

	private static async Task<RoomSnapshot> RecordForAsync(RecordingLoopHarness loop, StreamRequest request, TimeSpan duration)
	{
		RoomSnapshot recording = await ReceiveForAsync(loop, request, duration);
		await request.EndAsync();
		return recording;
	}

	private static async Task<RoomSnapshot> ReceiveForAsync(RecordingLoopHarness loop, StreamRequest request, TimeSpan duration)
	{
		await request.AcceptAsync();
		await request.CopyStarted;
		RoomSnapshot recording = loop.Snapshot;
		loop.Time.Advance(duration);

		// 看门狗采样过之后有数据的时长才会算到这一刻；时钟一次跨过多个采样周期时，后面的采样间隔为零，速度不是正数
		await loop.WaitUntilAsync(static snapshot => snapshot.BytesPerSecond is not 0);
		return recording;
	}

	[Test]
	[Timeout(30_000)]
	public async Task QuickFailuresTryTheNextHostThenTheNextCandidateWithoutWaitingAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.GoLive();

		StreamRequest first = await loop.NextStreamAsync();
		await EndImmediatelyAsync(first);
		StreamRequest second = await loop.NextStreamAsync();
		await RecordForAsync(loop, second, TimeSpan.FromSeconds(10));
		StreamRequest third = await loop.NextStreamAsync();
		await third.AcceptAsync(false);
		await loop.WaitUntilAsync(static snapshot => snapshot.LastError?.Contains("c.test", StringComparison.Ordinal) is true);

		using (Assert.Multiple())
		{
			await Assert.That(string.Join(", ", first.Host, second.Host, third.Host)).IsEqualTo("a.test, b.test, c.test");
			await Assert.That(loop.PlayInfoRequests).IsEqualTo(1);
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task EveryStreamFailingQuicklyWaitsTheReconnectDelayBeforeRequestingAgainAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.GoLive();

		for (int i = 0; i < 3; ++i)
		{
			await EndImmediatelyAsync(await loop.NextStreamAsync());
		}

		(StreamRequest next, TimeSpan advanced) = await loop.AdvanceUntilNextStreamAsync();

		using (Assert.Multiple())
		{
			await Assert.That(advanced).IsGreaterThanOrEqualTo(RecordingLoopHarness.ReconnectDelay);
			await Assert.That(next.Host).IsEqualTo("a.test");
			await Assert.That(loop.PlayInfoRequests).IsEqualTo(2);
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task EveryStreamFailingToOpenWaitsTheReconnectDelayBeforeRequestingAgainAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.GoLive();

		for (int i = 0; i < 3; ++i)
		{
			(await loop.NextStreamAsync()).Reject();
		}

		(StreamRequest next, TimeSpan advanced) = await loop.AdvanceUntilNextStreamAsync();

		using (Assert.Multiple())
		{
			await Assert.That(advanced).IsGreaterThanOrEqualTo(RecordingLoopHarness.ReconnectDelay);
			await Assert.That(next.Host).IsEqualTo("a.test");
			await Assert.That(loop.PlayInfoRequests).IsEqualTo(2);
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task EveryStreamFailingToOpenAfterTheRoomWentOfflineEndsTheRecordingAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.GoLive();

		StreamRequest first = await loop.NextStreamAsync();
		loop.GoOffline();
		first.Reject();

		for (int i = 0; i < 2; ++i)
		{
			(await loop.NextStreamAsync()).Reject();
		}

		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Idle);

		using (Assert.Multiple())
		{
			await Assert.That(loop.Snapshot.LiveStatus).IsEqualTo(LiveStatus.Idle);
			await Assert.That(loop.Snapshot.LastError).IsNull();
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task DisconnectAfterALongRecordingStartsOverFromThePreferredStreamAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.GoLive();

		await EndImmediatelyAsync(await loop.NextStreamAsync());
		StreamRequest healthy = await loop.NextStreamAsync();
		RoomSnapshot recording = await RecordForAsync(loop, healthy, RecordingAttempt.QuickFailureThreshold + TimeSpan.FromSeconds(1));
		StreamRequest next = await loop.NextStreamAsync();

		using (Assert.Multiple())
		{
			await Assert.That(healthy.Host).IsEqualTo("b.test");
			await Assert.That((recording.Recording?.Format, recording.Recording?.Codec, recording.Recording?.Quality)).IsEqualTo((StreamFormat.Flv, StreamCodec.Avc, 10000L));
			await Assert.That(next.Host).IsEqualTo("a.test");
			await Assert.That(loop.PlayInfoRequests).IsEqualTo(2);
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task DiskWriteFailureWaitsAndRetriesTheSameStreamAsync(CancellationToken cancellationToken)
	{
		Disk disk = new();
		await using RecordingLoopHarness loop = new(cancellationToken, disk.CreateFile);
		loop.GoLive();

		await EndImmediatelyAsync(await loop.NextStreamAsync());
		StreamRequest failing = await loop.NextStreamAsync();
		disk.Fill();
		await failing.AcceptAsync();
		await loop.WaitUntilAsync(static snapshot => snapshot.LastError?.Contains("disk full", StringComparison.Ordinal) is true);
		(StreamRequest next, TimeSpan advanced) = await loop.AdvanceUntilNextStreamAsync();

		using (Assert.Multiple())
		{
			await Assert.That(failing.Host).IsEqualTo("b.test");
			await Assert.That(advanced).IsGreaterThanOrEqualTo(RecordingLoopHarness.ReconnectDelay);
			await Assert.That(next.Host).IsEqualTo("b.test");
			await Assert.That(loop.PlayInfoRequests).IsEqualTo(1);
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task DiskWriteFailureWhileFFmpegRecordsWaitsAndRetriesTheSameStreamAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken, static _ => new FakeFile(static _ => ValueTask.FromException(new IOException("disk full"))), RecorderType.FFmpeg);
		loop.GoLive();

		StreamRequest first = await loop.NextStreamAsync();
		await first.AcceptAsync();
		await loop.WaitUntilAsync(static snapshot => snapshot.LastError?.Contains("disk full", StringComparison.Ordinal) is true);
		(StreamRequest next, TimeSpan advanced) = await loop.AdvanceUntilNextStreamAsync();

		using (Assert.Multiple())
		{
			await Assert.That(first.Host).IsEqualTo("a.test");
			await Assert.That(advanced).IsGreaterThanOrEqualTo(RecordingLoopHarness.ReconnectDelay);
			await Assert.That(next.Host).IsEqualTo("a.test");
			await Assert.That(loop.PlayInfoRequests).IsEqualTo(1);
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task DiskWriteFailureAfterALongRecordingStartsOverFromThePreferredStreamAsync(CancellationToken cancellationToken)
	{
		Disk disk = new();
		await using RecordingLoopHarness loop = new(cancellationToken, disk.CreateFile);
		loop.GoLive();

		await EndImmediatelyAsync(await loop.NextStreamAsync());
		StreamRequest healthy = await loop.NextStreamAsync();
		await ReceiveForAsync(loop, healthy, RecordingAttempt.QuickFailureThreshold + TimeSpan.FromSeconds(1));
		disk.Fill();
		await healthy.SendAsync();
		(StreamRequest next, TimeSpan advanced) = await loop.AdvanceUntilNextStreamAsync();

		using (Assert.Multiple())
		{
			await Assert.That(healthy.Host).IsEqualTo("b.test");
			await Assert.That(advanced).IsGreaterThanOrEqualTo(RecordingLoopHarness.ReconnectDelay);
			await Assert.That(next.Host).IsEqualTo("a.test");
			await Assert.That(loop.PlayInfoRequests).IsEqualTo(2);
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task DiskStallingUntilTheStreamLooksIdleIsAWriteFailureAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken, static _ => new FakeFile(static ct => new ValueTask(Task.Delay(Timeout.Infinite, ct))));
		loop.GoLive();

		StreamRequest stalled = await loop.NextStreamAsync();
		await stalled.AcceptAsync();
		Task streaming = stalled.StreamUntilClosedAsync();
		(StreamRequest next, TimeSpan advanced) = await loop.AdvanceUntilNextStreamAsync();
		await streaming;

		using (Assert.Multiple())
		{
			await Assert.That(advanced).IsGreaterThanOrEqualTo(loop.Settings.StreamIdleTimeout + RecordingLoopHarness.ReconnectDelay);
			await Assert.That(next.Host).IsEqualTo("a.test");
			await Assert.That(loop.Snapshot.LastError).Contains("did not finish");
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task StreamEndingBecauseTheRoomWentOfflineIsNotAFailureAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.GoLive();

		StreamRequest request = await loop.NextStreamAsync();
		await request.AcceptAsync();
		await request.CopyStarted;
		loop.GoOffline();
		await request.EndAsync();
		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Idle);

		using (Assert.Multiple())
		{
			await Assert.That(loop.Snapshot.LastError).IsNull();
			await Assert.That(loop.Snapshot.LiveStatus).IsEqualTo(LiveStatus.Idle);
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task UserStoppingTheRecordingIsNotAFailureAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.GoLive();

		StreamRequest request = await loop.NextStreamAsync();
		await request.AcceptAsync();
		await request.CopyStarted;
		loop.Session.UpdateSettings(loop.Settings with { IsRecordingEnabled = false });
		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Idle);

		using (Assert.Multiple())
		{
			await Assert.That(loop.Snapshot.LastError).IsNull();
			await Assert.That(loop.Snapshot.Recording).IsNull();
		}
	}

	// 写满之前一切正常
	private sealed class Disk
	{
		private volatile bool _isFull;

		public void Fill()
		{
			_isFull = true;
		}

		public Stream CreateFile(string path)
		{
			return new FakeFile(_ => _isFull ? ValueTask.FromException(new IOException("disk full")) : ValueTask.CompletedTask);
		}
	}

	private sealed class FakeFile(Func<CancellationToken, ValueTask> write) : Stream
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
}
