namespace BilibiliLiveRecordDownLoader.Application.Contracts.Rooms;

/// <param name="IsRecording">正在写弹幕文件，跟着视频分段或者仅录弹幕</param>
public sealed record RoomDanmakuSnapshot(bool IsRecording)
{
	public static RoomDanmakuSnapshot Idle { get; } = new(false);
}
