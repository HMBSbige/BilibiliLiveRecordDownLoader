namespace BilibiliLiveRecordDownLoader.Application.Contracts.Danmaku;

public enum DanmakuConnectionState
{
	Connecting,
	Connected,
	Disconnected
}

/// <param name="Connection">连上的次数；重连可能快到看不到连接中，界面靠它判断连接换过没有</param>
/// <param name="UserId">连接所用的登录 uid，匿名为 0；还没连上过时为 null</param>
/// <param name="IsMasked">这次连接收到了未登录看不到昵称的通知，之后的昵称打码、uid 多为 0</param>
/// <param name="MessagesPerMinute">最近一分钟收到的弹幕、SC、礼物和上舰条数</param>
/// <param name="DisconnectedAt">连上之后为 null</param>
public sealed record DanmakuFeedStatus(DanmakuConnectionState State, long Connection, long? UserId, bool IsMasked, double MessagesPerMinute, DateTimeOffset? DisconnectedAt);
