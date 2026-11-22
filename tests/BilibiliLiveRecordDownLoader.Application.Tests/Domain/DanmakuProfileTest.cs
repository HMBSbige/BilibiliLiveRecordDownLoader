namespace BilibiliLiveRecordDownLoader.Application.Tests.Domain;

public sealed class DanmakuProfileTest
{
	[Test]
	public async Task RoomOffsetOverridesTheGlobalOneAsync()
	{
		AppConfig app = AppConfig.CreateDefault();
		app.DanmakuOffset = TimeSpan.FromSeconds(2);

		DanmakuProfile inherited = DanmakuProfile.Create(app, new RoomConfig { RoomId = 1 });
		DanmakuProfile overridden = DanmakuProfile.Create(app, new RoomConfig { RoomId = 1, DanmakuOffset = TimeSpan.FromSeconds(-1) });

		using (Assert.Multiple())
		{
			await Assert.That(inherited.Offset).IsEqualTo(TimeSpan.FromSeconds(2));
			await Assert.That(overridden.Offset).IsEqualTo(TimeSpan.FromSeconds(-1));
		}
	}

	[Test]
	public async Task GlobalSettingsAndTheRoomSwitchAreCarriedOverAsync()
	{
		AppConfig app = new()
		{
			RecordSuperChats = false,
			GiftMode = DanmakuGiftMode.Paid,
			RecordGuards = false,
			GenerateDanmakuSubtitle = false,
			ShowPaidMessagesInSubtitle = false,
			SaveRawDanmaku = true,
			WriteChaptersOnConvert = true
		};

		DanmakuProfile profile = DanmakuProfile.Create(app, new RoomConfig { RoomId = 1, IsDanmakuEnabled = true });

		await Assert.That(profile).IsEqualTo(new DanmakuProfile(true, false, DanmakuGiftMode.Paid, false, TimeSpan.Zero, false, false, true, true));
	}
}
