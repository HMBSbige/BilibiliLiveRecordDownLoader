namespace BilibiliLiveRecordDownLoader.Application.Contracts.Danmaku;

// 所有成员线程安全、立即返回，不会回调到界面
public interface IDanmakuFeed : IDisposable
{
	long RoomId { get; }

	// 有新条目或连接状态变化时增加，接收速率的变化不计入
	long Version { get; }

	DanmakuFeedStatus Status { get; }

	// 按序号递增，最多 DanmakuConsts.MaxFeedEntries 条；落后太多时较早的已经丢弃，序号会跳过它们
	IReadOnlyList<LiveDanmakuEntryDto> GetEntries(long afterSequence);
}
