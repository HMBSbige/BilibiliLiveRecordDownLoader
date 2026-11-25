using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using Microsoft.Extensions.Logging;
using System.Net;

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
	[Arguments(true)]
	[Arguments(false)]
	public async Task EveryStreamOfAnUnverifiedBatchFailingWaitsTheReconnectDelayAsync(bool failsToOpen, CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.GoLive();

		for (int i = 0; i < 3; ++i)
		{
			StreamRequest request = await loop.NextStreamAsync();

			if (failsToOpen)
			{
				request.Reject();
			}
			else
			{
				await EndImmediatelyAsync(request);
			}
		}

		(StreamRequest next, TimeSpan advanced) = await loop.AdvanceUntilNextStreamAsync();

		using (Assert.Multiple())
		{
			await Assert.That(advanced).IsGreaterThanOrEqualTo(RecordingLoopHarness.ReconnectDelay).And.IsLessThan(RecordingLoopHarness.ReconnectDelay * 2);
			await Assert.That(next.Host).IsEqualTo("a.test");
			await Assert.That(loop.PlayInfoRequests).IsEqualTo(2);
		}
	}

	[Test]
	public async Task WaitingToReconnectShowsConnectingAndPostProcessesTheLastFileAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.GoLive();

		for (int i = 0; i < 3; ++i)
		{
			await EndImmediatelyAsync(await loop.NextStreamAsync());
		}

		await loop.WaitUntilAsync(static snapshot => snapshot.LastError?.Contains("c.test", StringComparison.Ordinal) is true);
		await loop.WaitUntilAsync(() => Directory.GetFiles(loop.RoomDirectory, "*.flv").Length is 0, "every recording is post-processed");

		using (Assert.Multiple())
		{
			await Assert.That(loop.Snapshot.RecordStatus).IsEqualTo(RecordStatus.Connecting);
			await Assert.That(loop.Snapshot.Recording).IsNull();
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
	public async Task DisconnectAfterALongRecordingStartsOverFromThePreferredHostWithTheSameUrlsAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.GoLive();

		await EndImmediatelyAsync(await loop.NextStreamAsync());
		StreamRequest healthy = await loop.NextStreamAsync();
		await loop.RecordForAsync(healthy, RecordingAttempt.QuickFailureThreshold + TimeSpan.FromSeconds(1));
		StreamRequest next = await loop.NextStreamAsync();

		using (Assert.Multiple())
		{
			await Assert.That(healthy.Host).IsEqualTo("b.test");
			await Assert.That(next.Host).IsEqualTo("a.test");
			await Assert.That(loop.StreamRequests.Last().Query).IsEqualTo("?sign=1");
			await Assert.That(loop.PlayInfoRequests).IsEqualTo(1);
		}
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task EveryStreamThatRecordedFailingRequestsNewUrlsWithoutWaitingAsync(bool failsToOpen, CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.GoLive();

		await loop.RecordForAsync(await loop.NextStreamAsync(), RecordingAttempt.QuickFailureThreshold + TimeSpan.FromSeconds(1));

		for (int i = 0; i < 3; ++i)
		{
			StreamRequest request = await loop.NextStreamAsync();

			if (failsToOpen)
			{
				request.Reject();
			}
			else
			{
				await EndImmediatelyAsync(request);
			}
		}

		// 不推进时钟也等得到，说明没有等重连间隔
		StreamRequest next = await loop.NextStreamAsync();

		using (Assert.Multiple())
		{
			await Assert.That(next.Host).IsEqualTo("a.test");
			await Assert.That(loop.StreamRequests.Last().Query).IsEqualTo("?sign=2");
			await Assert.That(loop.PlayInfoRequests).IsEqualTo(2);
		}
	}

	[Test]
	public async Task RejectedStreamOfARecordedBatchIsSignedAgainBeforeFallingBackAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.GoLive();

		await loop.RecordForAsync(await loop.NextStreamAsync(), RecordingAttempt.QuickFailureThreshold + TimeSpan.FromSeconds(1));
		(await loop.NextStreamAsync()).Reject(HttpStatusCode.Forbidden);
		(await loop.NextStreamAsync()).Reject(HttpStatusCode.Forbidden);
		// 不推进时钟也等得到，说明没有等重连间隔
		StreamRequest next = await loop.NextStreamAsync();

		using (Assert.Multiple())
		{
			await Assert.That(next.Host).IsEqualTo("a.test");
			await Assert.That(loop.StreamRequests.Last().Query).IsEqualTo("?sign=2");
			await Assert.That(loop.StreamRequests).DoesNotContain(static uri => uri.Host is "c.test");
			await Assert.That(loop.PlayInfoRequests).IsEqualTo(2);
		}
	}

	[Test]
	public async Task RejectedUrlOfARecordedBatchIsSignedAgainAfterTheNextRecordingAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.GoLive();

		await loop.RecordForAsync(await loop.NextStreamAsync(), RecordingAttempt.QuickFailureThreshold + TimeSpan.FromSeconds(1));
		(await loop.NextStreamAsync()).Reject(HttpStatusCode.Forbidden);
		await loop.RecordForAsync(await loop.NextStreamAsync(), RecordingAttempt.QuickFailureThreshold + TimeSpan.FromSeconds(1));
		StreamRequest next = await loop.NextStreamAsync();

		using (Assert.Multiple())
		{
			await Assert.That(next.Host).IsEqualTo("a.test");
			await Assert.That(loop.StreamRequests.Last().Query).IsEqualTo("?sign=2");
			await Assert.That(loop.PlayInfoRequests).IsEqualTo(2);
		}
	}

	[Test]
	public async Task DataStoppingBeforeTheThresholdIsAQuickFailureEvenIfTheConnectionLingersAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.GoLive();

		StreamRequest first = await loop.NextStreamAsync();
		await loop.ReceiveForAsync(first, TimeSpan.FromSeconds(20));
		(StreamRequest next, _) = await loop.AdvanceUntilNextStreamAsync();

		using (Assert.Multiple())
		{
			await Assert.That(first.Host).IsEqualTo("a.test");
			await Assert.That(next.Host).IsEqualTo("b.test");
			await Assert.That(loop.PlayInfoRequests).IsEqualTo(1);
		}
	}

	[Test]
	public async Task NewUrlsFailingToOpenAfterTheOldOnesWereRejectedWaitAsTheStreamerBeingSilentAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.GoLive();

		await loop.RecordForAsync(await loop.NextStreamAsync(), RecordingAttempt.QuickFailureThreshold + TimeSpan.FromSeconds(1));

		for (int i = 0; i < 2; ++i)
		{
			(await loop.NextStreamAsync()).Reject(HttpStatusCode.Forbidden);
		}

		for (int i = 0; i < 3; ++i)
		{
			(await loop.NextStreamAsync()).Reject();
		}

		(StreamRequest next, TimeSpan advanced) = await loop.AdvanceUntilNextStreamAsync();

		using (Assert.Multiple())
		{
			await Assert.That(advanced).IsGreaterThanOrEqualTo(RecordingLoopHarness.ReconnectDelay).And.IsLessThan(RecordingLoopHarness.ReconnectDelay * 2);
			await Assert.That(next.Host).IsEqualTo("a.test");
			await Assert.That(loop.PlayInfoRequests).IsEqualTo(3);
			await Assert.That(loop.Logs).Contains(static log => log.Exception is StreamUnavailableException { IsStreamerSilent: true });
		}
	}

	[Test]
	[Arguments(RecorderType.BuiltIn)]
	[Arguments(RecorderType.FFmpeg)]
	public async Task QuickDiskWriteFailureWaitsAndRetriesTheSameHostAsync(RecorderType recorderType, CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken, static _ => new FakeFile(static _ => ValueTask.FromException(new IOException("disk full"))), recorderType);
		loop.GoLive();

		StreamRequest first = await loop.NextStreamAsync();
		await first.AcceptAsync();
		await loop.WaitUntilAsync(static snapshot => snapshot.LastError?.Contains("disk full", StringComparison.Ordinal) is true);
		(StreamRequest next, TimeSpan advanced) = await loop.AdvanceUntilNextStreamAsync();

		using (Assert.Multiple())
		{
			await Assert.That(first.Host).IsEqualTo("a.test");
			await Assert.That(advanced).IsGreaterThanOrEqualTo(RecordingLoopHarness.ReconnectDelay).And.IsLessThan(RecordingLoopHarness.ReconnectDelay * 2);
			await Assert.That(next.Host).IsEqualTo("a.test");
			await Assert.That(loop.PlayInfoRequests).IsEqualTo(1);
		}
	}

	[Test]
	public async Task DiskWriteFailureAfterALongRecordingWaitsAndStartsOverFromThePreferredHostWithTheSameUrlsAsync(CancellationToken cancellationToken)
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
			await Assert.That(advanced).IsGreaterThanOrEqualTo(RecordingLoopHarness.ReconnectDelay).And.IsLessThan(RecordingLoopHarness.ReconnectDelay * 2);
			await Assert.That(next.Host).IsEqualTo("a.test");
			await Assert.That(loop.StreamRequests.Last().Query).IsEqualTo("?sign=1");
			await Assert.That(loop.PlayInfoRequests).IsEqualTo(1);
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

		await loop.Session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

		await Assert.That(ui.Posted).IsEqualTo(0);
	}
}
