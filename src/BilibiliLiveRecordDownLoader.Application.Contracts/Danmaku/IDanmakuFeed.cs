namespace BilibiliLiveRecordDownLoader.Application.Contracts.Danmaku;

/// <summary>
/// 供界面轮询：Version 变了就用上次处理到的序号取新条目，再读一次状态
/// 所有成员线程安全、立即返回，不会回调到界面
/// </summary>
public interface IDanmakuFeed : IDisposable
{
	long RoomId { get; }

	// 有新条目或连接状态变化时增加；接收速率随时间变化，不计入
	long Version { get; }

	DanmakuFeedStatus Status { get; }

	// 按序号递增，最多 DanmakuConsts.MaxFeedEntries 条；落后太多时较早的已经丢弃，序号会跳过它们
	// 房间在录弹幕时，订阅时就能取到之前最近的条目；只是查看时从订阅那一刻开始
	IReadOnlyList<LiveDanmakuEntryDto> GetEntries(long afterSequence);
}
