namespace BilibiliLiveRecordDownLoader.Application.Contracts.Rooms;

public sealed record RoomSnapshot
(
	RoomSettingsDto Settings,
	long ShortId,
	string? UserName,
	string? Title,
	LiveStatus LiveStatus,
	RecordStatus RecordStatus,
	double BytesPerSecond,
	ActiveRecordingDto? Recording,
	string? LastError
)
{
	public long RoomId => Settings.RoomId;

	public RoomDanmakuSnapshot Danmaku { get; init; } = RoomDanmakuSnapshot.Idle;
}
