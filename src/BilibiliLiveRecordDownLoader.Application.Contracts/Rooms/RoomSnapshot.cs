namespace BilibiliLiveRecordDownLoader.Application.Contracts.Rooms;

public sealed record RoomSnapshot
(
	RoomSettingsDto Settings,
	long ShortId,
	string? UserName,
	string? Title,
	LiveStatus LiveStatus,
	RecordStatus RecordStatus,
	double BytesPerSecond
)
{
	public long RoomId => Settings.RoomId;
}
