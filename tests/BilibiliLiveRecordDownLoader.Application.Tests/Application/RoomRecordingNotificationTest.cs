using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using System.Diagnostics;
using System.Net;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class RoomRecordingNotificationTest
{
	public enum StallReset
	{
		Recorded,
		WentOffline
	}

	public enum StreamerSilence
	{
		NoStreamListed,
		EveryStreamNotFound,
		ConnectedWithoutData
	}

	public enum DiskFailure
	{
		Full,
		Stalled
	}

	// 卡住的写入一直等到排空超时，或者过一阵以 IO 错误结束
	public enum StuckWrite
	{
		Hangs,
		FailsLater
	}

	[Test]
	public async Task EveryStreamFailingForAMinuteNotifiesOnceAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.GoLive();

		(StreamRequest next, TimeSpan quiet, TimeSpan notified) = await FailUntilNotifiedAsync(loop, await loop.NextStreamAsync(), 1);
		await FailForAsync(loop, next, TimeSpan.FromMinutes(2));
		ShownNotification shown = await Assert.That(loop.Notifications.Shown).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(quiet).IsLessThan(RecordingFaultTracker.StallThreshold);
			await Assert.That(notified).IsGreaterThanOrEqualTo(RecordingFaultTracker.StallThreshold);
			await Assert.That(shown.Title).Contains("streamer");
			await Assert.That(shown.Type).IsEqualTo(AppNotificationType.Error);
		}
	}

	[Test]
	[Arguments(StallReset.Recorded)]
	[Arguments(StallReset.WentOffline)]
	public async Task StallIsCountedAgainAfterAsync(StallReset reset, CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.GoLive();
		(StreamRequest next, _, _) = await FailUntilNotifiedAsync(loop, await loop.NextStreamAsync(), 1);

		switch (reset)
		{
			case StallReset.Recorded:
				await loop.RecordForAsync(next, RecordingAttempt.QuickFailureThreshold + TimeSpan.FromSeconds(1));
				break;
			case StallReset.WentOffline:
				loop.GoOffline();
				await FailEveryStreamAsync(loop, next);
				await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Idle);
				loop.GoLive();
				break;
			default:
				throw new UnreachableException();
		}

		(_, TimeSpan quiet, TimeSpan notified) = await FailUntilNotifiedAsync(loop, await loop.NextStreamAsync(), 2);

		using (Assert.Multiple())
		{
			await Assert.That(quiet).IsLessThan(RecordingFaultTracker.StallThreshold);
			await Assert.That(notified).IsGreaterThanOrEqualTo(RecordingFaultTracker.StallThreshold);
		}
	}

	[Test]
	[Arguments(StreamerSilence.NoStreamListed)]
	[Arguments(StreamerSilence.EveryStreamNotFound)]
	[Arguments(StreamerSilence.ConnectedWithoutData)]
	public async Task StreamerNotPushingIsNotNotifiedAsync(StreamerSilence silence, CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);

		if (silence is StreamerSilence.NoStreamListed)
		{
			loop.Streams = "[]";
		}

		loop.GoLive();
		DateTimeOffset start = loop.Time.GetUtcNow();

		switch (silence)
		{
			case StreamerSilence.NoStreamListed:
				while (loop.Time.GetUtcNow() - start < TimeSpan.FromMinutes(2))
				{
					await loop.AdvanceThroughReconnectDelayAsync();
				}

				// 开始等重连时，这一轮是否提醒已经定了
				await loop.Time.WaitForDelayAsync(RecordingLoopHarness.ReconnectDelay, cancellationToken);
				break;
			case StreamerSilence.EveryStreamNotFound:
				await FailForAsync(loop, await loop.NextStreamAsync(), TimeSpan.FromMinutes(2), HttpStatusCode.NotFound);
				break;
			case StreamerSilence.ConnectedWithoutData:
				StreamRequest next = await loop.NextStreamAsync();

				while (loop.Time.GetUtcNow() - start < TimeSpan.FromMinutes(2))
				{
					for (int line = 0; line < 3; ++line)
					{
						await loop.GoIdleAsync(next);
						next = line < 2 ? await loop.NextStreamAsync() : await loop.NextStreamAfterReconnectDelayAsync();
					}
				}

				break;
			default:
				throw new UnreachableException();
		}

		await Assert.That(loop.Notifications.Shown).IsEmpty();
	}

	[Test]
	public async Task StreamsThatKeepSendingDataInShortStretchesAreNotNotifiedAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.GoLive();
		StreamRequest next = await loop.NextStreamAsync();
		DateTimeOffset start = loop.Time.GetUtcNow();

		while (loop.Time.GetUtcNow() - start < TimeSpan.FromMinutes(3))
		{
			for (int line = 0; line < 3; ++line)
			{
				await loop.RecordForAsync(next, TimeSpan.FromSeconds(20));
				next = line < 2 ? await loop.NextStreamAsync() : await loop.NextStreamAfterReconnectDelayAsync();
			}
		}

		await Assert.That(loop.Notifications.Shown).IsEmpty();
	}

	[Test]
	public async Task StreamsSendingOnlyTheHeaderAreNotifiedAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.GoLive();

		(_, TimeSpan quiet, TimeSpan notified) = await FailUntilNotifiedAsync(loop, await loop.NextStreamAsync(), 1, SendOnlyTheHeaderAsync);

		using (Assert.Multiple())
		{
			await Assert.That(quiet).IsLessThan(RecordingFaultTracker.StallThreshold);
			await Assert.That(notified).IsGreaterThanOrEqualTo(RecordingFaultTracker.StallThreshold);
		}
	}

	[Test]
	public async Task DiskWriteFailureNotifiesOnceUntilWritingRecoversAsync(CancellationToken cancellationToken)
	{
		FakeDisk disk = new();
		await using RecordingLoopHarness loop = new(cancellationToken, disk.CreateFile);
		loop.GoLive();
		disk.Fill();

		StreamRequest next = await loop.NextStreamAsync();
		DateTimeOffset failingSince = loop.Time.GetUtcNow();

		while (loop.Time.GetUtcNow() - failingSince < TimeSpan.FromMinutes(2))
		{
			await next.AcceptAsync();
			next = await loop.NextStreamAfterReconnectDelayAsync();
		}

		ShownNotification first = await Assert.That(loop.Notifications.Shown).HasSingleItem();

		disk.Free();
		await loop.RecordForAsync(next, RecordingAttempt.QuickFailureThreshold + TimeSpan.FromSeconds(1));

		// 正常录制的那次要先写完，下一次拉流开始时才能再写满
		next = await loop.NextStreamAsync();
		disk.Fill();
		await next.AcceptAsync();
		await loop.NextStreamAfterReconnectDelayAsync();

		using (Assert.Multiple())
		{
			await Assert.That(first.Title).Contains("streamer");
			await Assert.That(first.Message).Contains("disk full");
			await Assert.That(first.Type).IsEqualTo(AppNotificationType.Error);
			await Assert.That(loop.Notifications.Shown).Count().IsEqualTo(2);
		}
	}

	[Test]
	[Arguments(DiskFailure.Full)]
	[Arguments(DiskFailure.Stalled)]
	public async Task DiskFailingDuringALongRecordingNotifiesAgainAsync(DiskFailure failure, CancellationToken cancellationToken)
	{
		FakeDisk disk = new();
		await using RecordingLoopHarness loop = new(cancellationToken, disk.CreateFile);
		loop.GoLive();
		disk.Fill();

		StreamRequest next = await loop.NextStreamAsync();
		await next.AcceptAsync();
		next = await loop.NextStreamAfterReconnectDelayAsync();
		await Assert.That(loop.Notifications.Shown).HasSingleItem();

		disk.Free();
		int writes = disk.Writes;
		await loop.ReceiveForAsync(next, RecordingAttempt.QuickFailureThreshold + TimeSpan.FromSeconds(1));

		// 写盘在后台进行，收到的数据写进去之后磁盘再出错，这次录制才算写盘正常过
		await loop.WaitUntilAsync(() => disk.Writes > writes, "the recording reaches the disk");

		switch (failure)
		{
			case DiskFailure.Full:
				disk.Fill();
				break;
			case DiskFailure.Stalled:
				disk.Stall();
				break;
			default:
				throw new UnreachableException();
		}

		await next.SendAsync();
		await next.EndAsync();
		await loop.NextStreamAfterReconnectDelayAsync();

		await Assert.That(loop.Notifications.Shown).Count().IsEqualTo(2);
	}

	[Test]
	[Arguments(StuckWrite.Hangs)]
	[Arguments(StuckWrite.FailsLater)]
	public async Task StuckDiskNotifiesOnceEvenIfTheNetworkKeepsReceivingAsync(StuckWrite write, CancellationToken cancellationToken)
	{
		FakeDisk disk = new();
		await using RecordingLoopHarness loop = new(cancellationToken, disk.CreateFile);
		disk.Stall();
		loop.GoLive();

		StreamRequest next = await loop.NextStreamAsync();

		for (int round = 0; round < 2; ++round)
		{
			await loop.ReceiveForAsync(next, RecordingAttempt.QuickFailureThreshold + TimeSpan.FromSeconds(1));

			if (write is StuckWrite.FailsLater)
			{
				disk.Fill();
				disk.Stall();
			}

			await next.EndAsync();
			next = await loop.NextStreamAfterReconnectDelayAsync();
		}

		await Assert.That(loop.Notifications.Shown).HasSingleItem();
	}

	[Test]
	public async Task RecordingFileThatCannotBeCreatedIsAWriteFailureAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken, static _ => throw new IOException("the network drive is gone"));
		loop.GoLive();

		StreamRequest next = await loop.NextStreamAsync();
		DateTimeOffset failingSince = loop.Time.GetUtcNow();

		while (loop.Time.GetUtcNow() - failingSince < TimeSpan.FromMinutes(2))
		{
			await next.AcceptAsync();
			next = await loop.NextStreamAfterReconnectDelayAsync();
		}

		ShownNotification shown = await Assert.That(loop.Notifications.Shown).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(shown.Message).Contains("the network drive is gone");
			await Assert.That(next.Host).IsEqualTo("a.test");
			await Assert.That(loop.PlayInfoRequests).IsEqualTo(1);
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task RecordingErrorsAreNotNotifiedWhenTurnedOffAsync(bool isDiskFull, CancellationToken cancellationToken)
	{
		FakeDisk disk = new();
		await using RecordingLoopHarness loop = new(cancellationToken, disk.CreateFile);
		loop.Config.NotifyOnRecordingError = false;
		loop.GoLive();

		StreamRequest next = await loop.NextStreamAsync();

		if (isDiskFull)
		{
			disk.Fill();
			await next.AcceptAsync();
			await loop.NextStreamAfterReconnectDelayAsync();
		}
		else
		{
			await FailForAsync(loop, next, TimeSpan.FromMinutes(2));
		}

		await Assert.That(loop.Notifications.Shown).IsEmpty();
	}

	private static async Task FailEveryStreamAsync(RecordingLoopHarness loop, StreamRequest first, HttpStatusCode status)
	{
		first.Reject(status);

		for (int i = 0; i < 2; ++i)
		{
			(await loop.NextStreamAsync()).Reject(status);
		}
	}

	// 全是 404 会被当成主播没在推流而不提醒，403 会让录满过的一批立即重新请求，都不是这里要测的
	private static Task FailEveryStreamAsync(RecordingLoopHarness loop, StreamRequest first)
	{
		return FailEveryStreamAsync(loop, first, HttpStatusCode.ServiceUnavailable);
	}

	private static async Task SendOnlyTheHeaderAsync(RecordingLoopHarness loop, StreamRequest first)
	{
		for (int line = 0; line < 3; ++line)
		{
			StreamRequest next = line is 0 ? first : await loop.NextStreamAsync();
			await next.AcceptAsync();
			await next.EndAsync();
		}
	}

	private static async Task FailForAsync(RecordingLoopHarness loop, StreamRequest next, TimeSpan duration, HttpStatusCode status = HttpStatusCode.ServiceUnavailable)
	{
		DateTimeOffset start = loop.Time.GetUtcNow();

		while (loop.Time.GetUtcNow() - start < duration)
		{
			await FailEveryStreamAsync(loop, next, status);
			next = await loop.NextStreamAfterReconnectDelayAsync();
		}
	}

	// 返回最后一轮没有提醒和出现提醒的那一轮距开始的时长
	private static async Task<(StreamRequest Next, TimeSpan Quiet, TimeSpan Notified)> FailUntilNotifiedAsync(RecordingLoopHarness loop, StreamRequest next, int count, Func<RecordingLoopHarness, StreamRequest, Task>? failRound = null)
	{
		failRound ??= FailEveryStreamAsync;
		DateTimeOffset start = loop.Time.GetUtcNow();
		TimeSpan quiet = TimeSpan.Zero;

		while (loop.Time.GetUtcNow() - start < RecordingFaultTracker.StallThreshold * 3)
		{
			TimeSpan failedAt = loop.Time.GetUtcNow() - start;
			await failRound(loop, next);

			// 录满过的一批全部失败时会立即重新请求，不等重连
			next = await loop.NextStreamAfterPossibleReconnectDelayAsync();

			if (loop.Notifications.Shown.Count >= count)
			{
				return (next, quiet, failedAt);
			}

			quiet = failedAt;
		}

		throw new TimeoutException($"No notification #{count} within {RecordingFaultTracker.StallThreshold * 3} of failures.");
	}
}
