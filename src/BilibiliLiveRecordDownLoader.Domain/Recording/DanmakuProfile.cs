namespace BilibiliLiveRecordDownLoader.Domain.Recording;

// IsEnabled 是房间的“录弹幕”；其余是全局设置，偏移可由房间覆盖
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
			DanmakuDefaults.ClampOffset(room.DanmakuOffset ?? app.DanmakuOffset),
			app.GenerateDanmakuSubtitle,
			app.ShowPaidMessagesInSubtitle,
			app.SaveRawDanmaku,
			app.WriteChaptersOnConvert
		);
	}
}
