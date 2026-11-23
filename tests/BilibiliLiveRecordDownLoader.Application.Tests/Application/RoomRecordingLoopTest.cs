using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using Microsoft.Extensions.Logging;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class RoomRecordingLoopTest
{
	private static async Task EndImmediatelyAsync(StreamRequest request)
	{
		await request.AcceptAsync();
		await request.EndAsync();
	}

	[Test]
	public async Task QuickFailuresTryTheNextHostThenTheNextCandidateWithoutWaitingAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.GoLive();

		StreamRequest first = await loop.NextStreamAsync();
		await EndImmediatelyAsync(first);
		StreamRequest second = await loop.NextStreamAsync();
		await loop.RecordForAsync(second, TimeSpan.FromSeconds(10));
		StreamRequest third = await loop.NextStreamAsync();
		await third.AcceptAsync(false);
		await loop.WaitUntilAsync(static snapshot => snapshot.LastError?.Contains("c.test", StringComparison.Ordinal) is true);

		using (Assert.Multiple())
		{
			await Assert.That((first.Host, second.Host, third.Host)).IsEqualTo(("a.test", "b.test", "c.test"));
			await Assert.That(loop.PlayInfoRequests).IsEqualTo(1);
		}
	}

	[Test]
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

	// 重连间隔最长可以设到一天，最后一个文件的转封装和弹幕收尾不能跟着等
	[Test]
	public async Task LastFileIsPostProcessedWithoutWaitingForTheReconnectDelayAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.GoLive();

		for (int i = 0; i < 3; ++i)
		{
			await EndImmediatelyAsync(await loop.NextStreamAsync());
		}

		// 所有线路都失败之后在等重连间隔，时钟不推进就一直停在这里；文件都只有几 KB，后处理会删掉它们
		await loop.WaitUntilAsync(static snapshot => snapshot.LastError?.Contains("c.test", StringComparison.Ordinal) is true);
		await loop.WaitUntilAsync(() => Directory.GetFiles(loop.RoomDirectory, "*.flv").Length is 0, "every recording is post-processed");

		await Assert.That(loop.Snapshot.RecordStatus).IsEqualTo(RecordStatus.Connecting);
	}

	[Test]
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
	public async Task DisconnectAfterALongRecordingStartsOverFromThePreferredStreamAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.GoLive();

		await EndImmediatelyAsync(await loop.NextStreamAsync());
		StreamRequest healthy = await loop.NextStreamAsync();
		RoomSnapshot recording = await loop.RecordForAsync(healthy, RecordingAttempt.QuickFailureThreshold + TimeSpan.FromSeconds(1));
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
	public async Task DiskWriteFailureWaitsAndRetriesTheSameStreamAsync(CancellationToken cancellationToken)
	{
		FakeDisk disk = new();
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
	public async Task DiskWriteFailureAfterALongRecordingStartsOverFromThePreferredStreamAsync(CancellationToken cancellationToken)
	{
		FakeDisk disk = new();
		await using RecordingLoopHarness loop = new(cancellationToken, disk.CreateFile);
		loop.GoLive();

		await EndImmediatelyAsync(await loop.NextStreamAsync());
		StreamRequest healthy = await loop.NextStreamAsync();
		await loop.ReceiveForAsync(healthy, RecordingAttempt.QuickFailureThreshold + TimeSpan.FromSeconds(1));
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
			await Assert.That(loop.Snapshot.LastError).IsEqualTo(AppLanguage.Current.RecordingWriteDrainTimedOut.ToString("1"));
		}
	}

	[Test]
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
	public async Task UserStoppingTheRecordingIsNotAFailureAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.GoLive();

		StreamRequest request = await loop.NextStreamAsync();
		await request.AcceptAsync();
		await request.CopyStarted;
		loop.Settings = loop.Settings with { IsRecordingEnabled = false };
		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Idle);

		using (Assert.Multiple())
		{
			await Assert.That(loop.Snapshot.LastError).IsNull();
			await Assert.That(loop.Snapshot.Recording).IsNull();
		}
	}

	[Test]
	public async Task WaitingToReconnectShowsConnectingWithTheFailureAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.GoLive();

		for (int i = 0; i < 3; ++i)
		{
			await EndImmediatelyAsync(await loop.NextStreamAsync());
		}

		// 所有线路都失败之后在等重连间隔，时钟不推进就一直停在这里
		await loop.WaitUntilAsync(static snapshot => snapshot.LastError?.Contains("c.test", StringComparison.Ordinal) is true);

		using (Assert.Multiple())
		{
			await Assert.That(loop.Snapshot.RecordStatus).IsEqualTo(RecordStatus.Connecting);
			await Assert.That(loop.Snapshot.Recording).IsNull();
		}
	}

	[Test]
	public async Task RecordingNormallyClearsTheEarlierFailureAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.GoLive();

		await EndImmediatelyAsync(await loop.NextStreamAsync());
		await loop.WaitUntilAsync(static snapshot => snapshot.LastError?.Contains("a.test", StringComparison.Ordinal) is true);
		StreamRequest healthy = await loop.NextStreamAsync();
		await loop.ReceiveForAsync(healthy, RecordingAttempt.QuickFailureThreshold + TimeSpan.FromSeconds(1));
		loop.GoOffline();
		await healthy.EndAsync();
		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Idle);

		await Assert.That(loop.Snapshot.LastError).IsNull();
	}

	[Test]
	public async Task StoppingWhileTheDiskIsStuckIsNotAFailureAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken, static _ => new FakeFile(static ct => new ValueTask(Task.Delay(Timeout.Infinite, ct))));
		loop.GoLive();

		StreamRequest stuck = await loop.NextStreamAsync();
		await stuck.AcceptAsync();
		await stuck.CopyStarted;
		Task streaming = stuck.StreamUntilClosedAsync();

		// 收到的数据在排空时限内写不完，收尾抛出写盘失败
		loop.Settings = loop.Settings with { IsRecordingEnabled = false };
		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Idle);
		await streaming;

		using (Assert.Multiple())
		{
			await Assert.That(loop.Snapshot.LastError).IsNull();
			await Assert.That(loop.Logs.Where(static log => log.Level >= LogLevel.Error)).IsEmpty();
			await Assert.That(loop.Logs).Contains(static log => log.Exception is RecordingWriteException);
		}
	}

	[Test]
	public async Task OnlyRecordingDanmakuCountsAsRecordingAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.Settings = loop.Settings with { IsDanmakuEnabled = true, IsRecordingEnabled = false };

		loop.GoLive();

		using (Assert.Multiple())
		{
			await Assert.That(loop.Snapshot.RecordStatus).IsEqualTo(RecordStatus.Idle);
			await Assert.That(loop.Session.IsRecording).IsTrue();
		}
	}

	[Test]
	public async Task LoopsStartedFromTheUIThreadDoNotRunOnItAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		StoppedSynchronizationContext ui = new();

		ui.Run(() =>
		{
			loop.Session.Start();
			loop.GoLive();
		});
		StreamRequest request = await loop.NextStreamAsync();
		await request.AcceptAsync();
		await request.CopyStarted;
		ui.Run(() => loop.Settings = loop.Settings with { IsRecordingEnabled = false });
		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Idle);

		// 循环的续体投回已经停止的 UI 线程时，释放会一直等下去
		await loop.Session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

		await Assert.That(ui.Posted).IsEqualTo(0);
	}
}
