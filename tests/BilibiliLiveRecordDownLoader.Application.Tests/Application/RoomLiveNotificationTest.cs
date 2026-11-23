using BilibiliLive.Danmu;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class RoomLiveNotificationTest
{
	private static readonly Uri LiveRoomUri = new($"https://live.bilibili.com/{RecordingLoopHarness.RoomId}");

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task LiveStartIsNotifiedWithTheRoomLinkWhenTurnedOnAsync(bool isNotificationEnabled, CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken, isNotificationEnabled: isNotificationEnabled);
		loop.Settings = loop.Settings with { IsRecordingEnabled = false };

		loop.GoLive();
		// 仍在直播中，不再提醒
		loop.GoLive();

		ShownNotification[] expected = isNotificationEnabled ? [new(AppLanguage.Current.LiveStarted.ToString("streamer"), "title", AppNotificationType.Information, LiveRoomUri)] : [];
		await Assert.That(loop.Notifications.Shown).IsEquivalentTo(expected, CollectionOrdering.Matching);
	}

	[Test]
	public async Task RefreshNotifiesOnlyWhenTheTitleReallyChangesAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = CreateOffline(cancellationToken);

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
	public async Task RoomChangedMessageNotifiesOnlyWhenTheTitleReallyChangesAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = CreateOffline(cancellationToken);

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
	[Arguments(false, true)]
	[Arguments(true, false)]
	public async Task TitleChangeIsNotNotifiedUnlessBothSwitchesAreOnAsync(bool isRoomNotificationEnabled, bool isTitleNotificationEnabled, CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = CreateOffline(cancellationToken, isRoomNotificationEnabled, isTitleNotificationEnabled);

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
	private static RecordingLoopHarness CreateOffline(CancellationToken cancellationToken, bool isRoomNotificationEnabled = true, bool isTitleNotificationEnabled = true)
	{
		RecordingLoopHarness loop = new(cancellationToken, isNotificationEnabled: isRoomNotificationEnabled);
		loop.Config.NotifyOnTitleChange = isTitleNotificationEnabled;
		loop.GoOffline();
		return loop;
	}
}
