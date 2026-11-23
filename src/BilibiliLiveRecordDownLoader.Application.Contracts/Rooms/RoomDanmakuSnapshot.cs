namespace BilibiliLiveRecordDownLoader.Application.Contracts.Rooms;

public sealed record RoomDanmakuSnapshot(bool IsRecording)
{
	public static RoomDanmakuSnapshot Idle { get; } = new(false);
}
