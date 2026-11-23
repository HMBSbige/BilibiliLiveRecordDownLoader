using BilibiliLive.Danmu;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class RoomTitleNotificationTest
{
	private static readonly Uri LiveRoomUri = new($"https://live.bilibili.com/{RecordingLoopHarness.RoomId}");

	[Test]
	[Timeout(30_000)]
	public async Task RefreshNotifiesOnlyWhenTheTitleReallyChangesAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = Create(cancellationToken);

		await loop.Session.RefreshAsync(cancellationToken);
		await loop.Session.RefreshAsync(cancellationToken);
		int afterFirstTitle = loop.Notifications.Shown.Count;
		loop.Title = "new title";
		await loop.Session.RefreshAsync(cancellationToken);
		await loop.Session.RefreshAsync(cancellationToken);
		ShownNotification shown = await Assert.That(loop.Notifications.Shown).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(afterFirstTitle).IsEqualTo(0);
			await Assert.That(shown.Title).Contains("streamer");
			await Assert.That(shown.Message).IsEqualTo("new title");
			await Assert.That(shown.Link).IsEqualTo(LiveRoomUri);
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task RoomChangedMessageNotifiesOnlyWhenTheTitleReallyChangesAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = Create(cancellationToken);

		loop.Session.Handle(new DanmuMessage.RoomChanged("title", null, null));
		int afterFirstTitle = loop.Notifications.Shown.Count;
		await loop.Session.RefreshAsync(cancellationToken);
		loop.Session.Handle(new DanmuMessage.RoomChanged("danmu title", "area", "parent"));
		loop.Session.Handle(new DanmuMessage.RoomChanged("danmu title", "area", "parent"));
		ShownNotification shown = await Assert.That(loop.Notifications.Shown).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(afterFirstTitle).IsEqualTo(0);
			await Assert.That(shown.Message).IsEqualTo("danmu title");
			await Assert.That(shown.Link).IsEqualTo(LiveRoomUri);
		}
	}

	[Test]
	[Timeout(30_000)]
	[Arguments(false, true)]
	[Arguments(true, false)]
	public async Task TitleChangeIsNotNotifiedUnlessBothSwitchesAreOnAsync(bool isRoomNotificationEnabled, bool isTitleNotificationEnabled, CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = Create(cancellationToken, isRoomNotificationEnabled, isTitleNotificationEnabled);

		await loop.Session.RefreshAsync(cancellationToken);
		loop.Title = "new title";
		await loop.Session.RefreshAsync(cancellationToken);
		loop.Session.Handle(new DanmuMessage.RoomChanged("danmu title", null, null));

		using (Assert.Multiple())
		{
			await Assert.That(loop.Snapshot.Title).IsEqualTo("danmu title");
			await Assert.That(loop.Notifications.Shown).IsEmpty();
		}
	}

	// 未开播，刷新时不会开始录制
	private static RecordingLoopHarness Create(CancellationToken cancellationToken, bool isRoomNotificationEnabled = true, bool isTitleNotificationEnabled = true)
	{
		RecordingLoopHarness loop = new(cancellationToken, isNotificationEnabled: isRoomNotificationEnabled);
		loop.Config.NotifyOnTitleChange = isTitleNotificationEnabled;
		loop.GoOffline();
		return loop;
	}
}
