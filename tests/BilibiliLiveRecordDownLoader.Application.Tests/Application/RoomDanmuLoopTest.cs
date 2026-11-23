using BilibiliLive.Danmu;
using BilibiliLiveRecordDownLoader.Application.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using System.Net;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class RoomDanmuLoopTest
{
	private const string OfflineRoomInfo = """{"code":0,"data":{"by_room_ids":{"22907643":{"room_id":22907643,"uid":1,"uname":"streamer","title":"title","live_status":0}}}}""";

	private static readonly DanmuSender Viewer = new(42, "观众昵称", GuardLevel.None, null);

	[Test]
	public async Task ChangingTheMonitorIntervalsKeepsTheDanmuConnectionAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		FakeDanmuConnection danmu = await ConnectAsync(loop);

		loop.Settings = loop.Settings with { StatusCheckInterval = loop.Settings.StatusCheckInterval * 2, DanmuReconnectDelay = loop.Settings.DanmuReconnectDelay * 2 };

		await danmu.SendAsync(Comment(Viewer, "改设置之后"));

		await Assert.That(loop.Session.Danmaku.Feed.Status.DisconnectedAt).IsNull();
	}

	[Test]
	public async Task ConnectionsDroppedRightAfterAuthenticationKeepBackingOffAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);

		loop.GoOffline();
		loop.Session.Start();
		FakeDanmuConnection danmu = await loop.Danmu.NextConnectionAsync();
		TimeSpan waited = TimeSpan.Zero;

		for (int i = 0; i < 2; ++i)
		{
			await danmu.SendAsync(new DanmuMessage.Connected(0));
			await danmu.SendAsync(new DanmuMessage.Raw("LOG_IN_NOTICE", """{"cmd":"LOG_IN_NOTICE"}"""u8.ToArray()));
			await danmu.SendAsync(new DanmuMessage.LoginNotice());
			danmu.Close();
			(danmu, waited) = await loop.AdvanceUntilDanmuReconnectedAsync();
		}

		await Assert.That(waited).IsGreaterThanOrEqualTo(loop.Settings.DanmuReconnectDelay * 2 * ReconnectBackoff.MinJitter);
	}

	[Test]
	public async Task LiveStartedFromDanmuDoesNotRecordThePreviousLiveIdAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.Settings = loop.Settings with { IsDanmakuEnabled = true };

		// 未开播时查到的 live_id 是上一场的
		loop.GoOffline();
		FakeDanmuConnection danmu = await ConnectAsync(loop);
		await loop.WaitUntilAsync(static snapshot => snapshot.LiveStatus is LiveStatus.Idle);

		loop.StartStreaming();
		await danmu.SendAsync(new DanmuMessage.LiveStarted());
		await StartRecordingAsync(loop);
		await loop.StopAsync();

		string path = await Assert.That(Directory.GetFiles(loop.RoomDirectory, "*.xml")).HasSingleItem();
		DanmakuFile file = await DanmakuXmlReader.ReadFileAsync(path, cancellationToken);

		await Assert.That(file.Source)
			.IsTypeOf<DanmakuFileSource.Native>()
			.And.Member(static source => source.Header.LiveId, static liveId => liveId.IsEmpty())
			.And.Member(static source => source.Header.LiveStartTime, static startTime => startTime.IsNull());
	}

	[Test]
	public async Task StoppingWaitsForThePostProcessingOfTheLastSegmentAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.Settings = loop.Settings with { IsDanmakuEnabled = true };
		FakeDanmuConnection danmu = await ConnectAsync(loop);
		await StartRecordingAsync(loop);
		await danmu.SendAsync(Comment(Viewer, "最后一段"));

		await loop.StopAsync();

		await Assert.That(Directory.GetFiles(loop.RoomDirectory, "*.ass")).HasSingleItem();
	}

	[Test]
	public async Task StatusCheckFailingAsTheRoomStopsDoesNotFailTheStopAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		TaskCompletionSource<Task> stopping = new(TaskCreationOptions.RunContinuationsAsynchronously);

		loop.RoomInfo = _ => new HttpResponseMessage(HttpStatusCode.PreconditionFailed)
		{
			Content = new DeferredContent("{}", async ct =>
			{
				stopping.TrySetResult(loop.Session.DisposeAsync().AsTask());
				await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
			})
		};

		loop.Session.Start();
		Task stopped = await stopping.Task.WaitAsync(cancellationToken);

		await Assert.That(async () => await stopped).ThrowsNothing();
	}

	[Test]
	public async Task StatusAnsweredAfterTheDanmuLiveStartDoesNotEndTheLiveAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.Settings = loop.Settings with { IsDanmakuEnabled = true, IsRecordingEnabled = false };
		loop.GoOffline();
		FakeDanmuConnection danmu = await ConnectAsync(loop);
		await loop.WaitUntilAsync(static snapshot => snapshot.LiveStatus is LiveStatus.Idle);
		TaskCompletionSource answered = new(TaskCreationOptions.RunContinuationsAsynchronously);
		loop.RoomInfo = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new DeferredContent(OfflineRoomInfo, answered.Task.WaitAsync) };

		Task refresh = loop.Session.RefreshAsync(cancellationToken);
		await danmu.SendAsync(new DanmuMessage.LiveStarted());
		answered.SetResult();
		await refresh;

		using (Assert.Multiple())
		{
			await Assert.That(loop.Snapshot.LiveStatus).IsEqualTo(LiveStatus.Live);
			await Assert.That(loop.Session.IsRecording).IsTrue();
		}
	}

	[Test]
	public async Task StatusAnsweredAfterTheDanmuTitleChangeKeepsTheNewTitleAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken, isNotificationEnabled: true);
		loop.Config.NotifyOnTitleChange = true;
		loop.GoOffline();
		await loop.Session.RefreshAsync(cancellationToken);
		TaskCompletionSource answered = new(TaskCreationOptions.RunContinuationsAsynchronously);
		loop.RoomInfo = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new DeferredContent(OfflineRoomInfo, answered.Task.WaitAsync) };

		Task refresh = loop.Session.RefreshAsync(cancellationToken);
		loop.Session.Handle(new DanmuMessage.RoomChanged("danmu title", null, null));
		answered.SetResult();
		await refresh;

		using (Assert.Multiple())
		{
			await Assert.That(loop.Snapshot.Title).IsEqualTo("danmu title");
			await Assert.That(loop.Notifications.Shown.Select(static shown => shown.Message)).IsEquivalentTo(["danmu title"]);
		}
	}

	[Test]
	public async Task DanmakuContentAndViewersStayOutOfTheLogsAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.Settings = loop.Settings with { IsDanmakuEnabled = true };
		FakeDanmuConnection danmu = await ConnectAsync(loop);
		await StartRecordingAsync(loop);

		await danmu.SendAsync(Comment(Viewer, "弹幕内容"));
		await danmu.SendAsync(new DanmuMessage.SuperChat(1, Viewer, "醒目留言内容", 30000, TimeSpan.FromSeconds(60)));
		await danmu.SendAsync(new DanmuMessage.Gift(Viewer, "礼物名称", 1, 1000, DanmuCoinType.Gold));
		await danmu.SendAsync(new DanmuMessage.GuardBought(Viewer, GuardLevel.Captain, 1, 198000, false));
		await loop.StopAsync();

		string[] logs = [.. loop.Logs.Select(static log => $"{log.Message} {log.Exception}")];
		string[] secrets = [Viewer.Name, "弹幕内容", "醒目留言内容", "礼物名称", "sign="];

		using (Assert.Multiple())
		{
			await Assert.That(logs).IsNotEmpty();
			await Assert.That(logs.Where(log => Array.Exists(secrets, secret => log.Contains(secret, StringComparison.Ordinal)))).IsEmpty();
		}
	}

	private static async Task<FakeDanmuConnection> ConnectAsync(RecordingLoopHarness loop)
	{
		loop.Session.Start();
		FakeDanmuConnection danmu = await loop.Danmu.NextConnectionAsync();
		await danmu.SendAsync(new DanmuMessage.Connected(0));
		return danmu;
	}

	// 录下的视频要超过后处理删除的下限
	private static async Task StartRecordingAsync(RecordingLoopHarness loop)
	{
		StreamRequest stream = await loop.NextStreamAsync();
		await stream.AcceptAsync([.. FlvSamples.Header, .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0), .. KeyFrame(0)]);
		loop.Time.Advance(TimeSpan.FromSeconds(1));
		await stream.SendAndWaitAsync(KeyFrame(1000));
	}

	private static byte[] KeyFrame(uint timestamp)
	{
		return FlvSamples.PaddedVideo(VideoPacket.KeyFrame, timestamp, 40 << 10);
	}

	private static DanmuMessage.Comment Comment(DanmuSender sender, string text)
	{
		return new DanmuMessage.Comment(sender, text, 1, 25, 0xFFFFFF, DateTimeOffset.UnixEpoch, "a16fe0dd", false);
	}

	private sealed class DeferredContent(string body, Func<CancellationToken, Task> wait) : HttpContent
	{
		protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
		{
			await wait(cancellationToken);
			await stream.WriteAsync(Encoding.UTF8.GetBytes(body), CancellationToken.None);
		}

		protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
		{
			throw new NotSupportedException("The content is only buffered with a cancellation token.");
		}

		protected override bool TryComputeLength(out long length)
		{
			length = 0;
			return false;
		}
	}
}
