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
		WentOffline,
		RecordingDisabled
	}

	public enum StreamerSilence
	{
		NoStreamListed,
		EveryStreamNotFound,
		ConnectedWithoutData
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
				loop.Settings = loop.Settings with { IsRecordingEnabled = false };
				await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Idle);
				loop.Settings = loop.Settings with { IsRecordingEnabled = true };
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

	// 主播重开推流软件、换网络时，直播间还会显示直播中一段时间，不是要人处理的故障
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
					await AdvanceUntilCandidatesRequestedAsync(loop);
				}

				// 下一次请求开始时上一次的失败已经处理完
				await AdvanceUntilCandidatesRequestedAsync(loop);
				break;
			case StreamerSilence.EveryStreamNotFound:
				await FailForAsync(loop, await loop.NextStreamAsync(), TimeSpan.FromMinutes(2), HttpStatusCode.NotFound);
				break;
			case StreamerSilence.ConnectedWithoutData:
				StreamRequest next = await loop.NextStreamAsync();

				while (loop.Time.GetUtcNow() - start < TimeSpan.FromMinutes(2))
				{
					await next.AcceptAsync();
					(next, _) = await loop.AdvanceUntilNextStreamAsync();
				}

				break;
			default:
				throw new UnreachableException();
		}

		await Assert.That(loop.Notifications.Shown).IsEmpty();
	}

	// 每次都算快速失败，但文件一直在增长，不是线路都坏了
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
				next = line < 2 ? await loop.NextStreamAsync() : (await loop.AdvanceUntilNextStreamAsync(RecordingLoopHarness.ReconnectDelay)).Request;
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
			await Assert.That(loop.Notifications.Shown).Count().IsEqualTo(2);
		}
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

	private static async Task FailEveryStreamAsync(RecordingLoopHarness loop, StreamRequest first, HttpStatusCode status)
	{
		first.Reject(status);

		for (int i = 0; i < 2; ++i)
		{
			(await loop.NextStreamAsync()).Reject(status);
		}
	}

	// 全是 404 会被当成主播没在推流，不提醒
	private static Task FailEveryStreamAsync(RecordingLoopHarness loop, StreamRequest first)
	{
		return FailEveryStreamAsync(loop, first, HttpStatusCode.Forbidden);
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

	private static async Task FailForAsync(RecordingLoopHarness loop, StreamRequest next, TimeSpan duration, HttpStatusCode status = HttpStatusCode.Forbidden)
	{
		DateTimeOffset start = loop.Time.GetUtcNow();

		while (loop.Time.GetUtcNow() - start < duration)
		{
			await FailEveryStreamAsync(loop, next, status);
			(next, _) = await loop.AdvanceUntilNextStreamAsync(RecordingLoopHarness.ReconnectDelay);
		}
	}

	// 拿不到候选流时没有拉流请求可等
	private static async Task AdvanceUntilCandidatesRequestedAsync(RecordingLoopHarness loop)
	{
		int requested = loop.PlayInfoRequests;

		for (int step = 0; step < 60 && loop.PlayInfoRequests == requested; ++step)
		{
			await Task.Delay(20);
			loop.Time.Advance(TimeSpan.FromSeconds(1));
		}

		await loop.WaitUntilAsync(_ => loop.PlayInfoRequests > requested);
	}

	// 下一次拉流开始时上一轮的失败已经处理完；返回最后一轮没有提醒和出现提醒的那一轮距开始的时长
	private static async Task<(StreamRequest Next, TimeSpan Quiet, TimeSpan Notified)> FailUntilNotifiedAsync(RecordingLoopHarness loop, StreamRequest next, int count, Func<RecordingLoopHarness, StreamRequest, Task>? failRound = null)
	{
		failRound ??= FailEveryStreamAsync;
		DateTimeOffset start = loop.Time.GetUtcNow();
		TimeSpan quiet = TimeSpan.Zero;

		while (true)
		{
			TimeSpan failedAt = loop.Time.GetUtcNow() - start;
			await failRound(loop, next);
			(next, _) = await loop.AdvanceUntilNextStreamAsync(RecordingLoopHarness.ReconnectDelay);

			if (loop.Notifications.Shown.Count >= count)
			{
				return (next, quiet, failedAt);
			}

			quiet = failedAt;
		}
	}
}
