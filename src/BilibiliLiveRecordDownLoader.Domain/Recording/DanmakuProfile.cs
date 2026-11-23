namespace BilibiliLiveRecordDownLoader.Domain.Recording;

public sealed record DanmakuProfile
(
	bool IsEnabled,
	bool RecordSuperChats,
	DanmakuGiftMode GiftMode,
	bool RecordGuards,
	TimeSpan Offset,
	bool GenerateSubtitle,
	bool ShowPaidMessagesInSubtitle,
	bool SaveRaw,
	bool WriteChapters
)
{
	public static DanmakuProfile Create(AppConfig app, RoomConfig room)
	{
		return new DanmakuProfile
		(
			room.IsDanmakuEnabled,
			app.RecordSuperChats,
			app.GiftMode,
			app.RecordGuards,
			room.DanmakuOffset ?? app.DanmakuOffset,
			app.GenerateDanmakuSubtitle,
			app.ShowPaidMessagesInSubtitle,
			app.SaveRawDanmaku,
			app.WriteChaptersOnConvert
		);
	}
}
