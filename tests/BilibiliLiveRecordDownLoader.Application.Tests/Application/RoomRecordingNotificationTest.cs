using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class RoomRecordingNotificationTest
{
	public enum StallReset
	{
		Recorded,
		WentOffline,
		RecordingDisabled
	}

	[Test]
	[Timeout(30_000)]
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
	[Timeout(30_000)]
	[Arguments(StallReset.Recorded)]
	[Arguments(StallReset.WentOffline)]
	[Arguments(StallReset.RecordingDisabled)]
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
			case StallReset.RecordingDisabled:
				loop.Session.UpdateSettings(loop.Settings with { IsRecordingEnabled = false });
				await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Idle);
				loop.Session.UpdateSettings(loop.Settings);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(reset));
		}

		(_, TimeSpan quiet, TimeSpan notified) = await FailUntilNotifiedAsync(loop, await loop.NextStreamAsync(), 2);

		using (Assert.Multiple())
		{
			await Assert.That(quiet).IsLessThan(RecordingFaultTracker.StallThreshold);
			await Assert.That(notified).IsGreaterThanOrEqualTo(RecordingFaultTracker.StallThreshold);
		}
	}

	[Test]
	[Timeout(30_000)]
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
			(next, _) = await loop.AdvanceUntilNextStreamAsync(RecordingLoopHarness.ReconnectDelay);
		}

		ShownNotification first = await Assert.That(loop.Notifications.Shown).HasSingleItem();

		disk.Free();
		await loop.RecordForAsync(next, RecordingAttempt.QuickFailureThreshold + TimeSpan.FromSeconds(1));

		// 正常录制的那次要先写完，下一次拉流开始时才能再写满
		next = await loop.NextStreamAsync();
		disk.Fill();
		await next.AcceptAsync();
		await loop.AdvanceUntilNextStreamAsync(RecordingLoopHarness.ReconnectDelay);

		using (Assert.Multiple())
		{
			await Assert.That(first.Title).Contains("streamer");
			await Assert.That(first.Message).Contains("disk full");
			await Assert.That(first.Type).IsEqualTo(AppNotificationType.Error);
			await Assert.That(loop.Notifications.Shown.Count).IsEqualTo(2);
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task RecordingFileThatCannotBeCreatedIsAWriteFailureAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken, static _ => throw new IOException("the network drive is gone"));
		loop.GoLive();

		StreamRequest next = await loop.NextStreamAsync();
		DateTimeOffset failingSince = loop.Time.GetUtcNow();

		while (loop.Time.GetUtcNow() - failingSince < TimeSpan.FromMinutes(2))
		{
			await next.AcceptAsync();
			(next, _) = await loop.AdvanceUntilNextStreamAsync(RecordingLoopHarness.ReconnectDelay);
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
	[Timeout(30_000)]
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
			await loop.AdvanceUntilNextStreamAsync(RecordingLoopHarness.ReconnectDelay);
		}
		else
		{
			await FailForAsync(loop, next, TimeSpan.FromMinutes(2));
		}

		await Assert.That(loop.Notifications.Shown).IsEmpty();
	}

	// 一轮里所有线路都打不开
	private static async Task FailEveryStreamAsync(RecordingLoopHarness loop, StreamRequest first)
	{
		first.Reject();

		for (int i = 0; i < 2; ++i)
		{
			(await loop.NextStreamAsync()).Reject();
		}
	}

	private static async Task<StreamRequest> FailForAsync(RecordingLoopHarness loop, StreamRequest next, TimeSpan duration)
	{
		DateTimeOffset start = loop.Time.GetUtcNow();

		while (loop.Time.GetUtcNow() - start < duration)
		{
			await FailEveryStreamAsync(loop, next);
			(next, _) = await loop.AdvanceUntilNextStreamAsync(RecordingLoopHarness.ReconnectDelay);
		}

		return next;
	}

	// 下一次拉流开始时上一轮的失败已经处理完；返回最后一轮没有提醒和出现提醒的那一轮距开始的时长
	private static async Task<(StreamRequest Next, TimeSpan Quiet, TimeSpan Notified)> FailUntilNotifiedAsync(RecordingLoopHarness loop, StreamRequest next, int count)
	{
		DateTimeOffset start = loop.Time.GetUtcNow();
		TimeSpan quiet = TimeSpan.Zero;

		while (true)
		{
			TimeSpan failedAt = loop.Time.GetUtcNow() - start;
			await FailEveryStreamAsync(loop, next);
			(next, _) = await loop.AdvanceUntilNextStreamAsync(RecordingLoopHarness.ReconnectDelay);

			if (loop.Notifications.Shown.Count >= count)
			{
				return (next, quiet, failedAt);
			}

			quiet = failedAt;
		}
	}
}
