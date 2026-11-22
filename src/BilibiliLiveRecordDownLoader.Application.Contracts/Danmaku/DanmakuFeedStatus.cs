namespace BilibiliLiveRecordDownLoader.Application.Contracts.Danmaku;

public enum DanmakuConnectionState
{
	Connecting,
	Connected,
	// 连接断开，等待重连
	Disconnected
}

/// <param name="UserId">连接所用的登录 uid，匿名为 0；还没连上过时为 null。</param>
/// <param name="IsMasked">这次连接收到了未登录看不到昵称的通知，之后的昵称打码、uid 多为 0。</param>
/// <param name="MessagesPerMinute">最近一分钟收到的弹幕、SC、礼物和上舰条数，读取时计算。</param>
/// <param name="DisconnectedAt">上一次连接断开的时刻，连上之后为 null。</param>
/// <param name="IsRecording">正在写弹幕文件。</param>
public sealed record DanmakuFeedStatus(DanmakuConnectionState State, long? UserId, bool IsMasked, double MessagesPerMinute, DateTimeOffset? DisconnectedAt, bool IsRecording);
