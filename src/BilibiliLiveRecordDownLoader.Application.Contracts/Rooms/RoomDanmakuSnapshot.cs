namespace BilibiliLiveRecordDownLoader.Application.Contracts.Rooms;

/// <param name="IsRecording">正在写弹幕文件，跟着视频分段或者仅录弹幕。</param>
/// <param name="UserId">弹幕连接所用的登录 uid，匿名为 0；还没连上过时为 null。</param>
/// <param name="IsMasked">弹幕连接收到了未登录看不到昵称的通知。</param>
public sealed record RoomDanmakuSnapshot(bool IsRecording, long? UserId, bool IsMasked)
{
	public static RoomDanmakuSnapshot Idle { get; } = new(false, null, false);
}
