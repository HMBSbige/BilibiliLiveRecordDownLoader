namespace BilibiliLiveRecordDownLoader.Application.Contracts.Danmaku;

// 价格都是千分之一元
public closed record DanmakuEntryDto
{
	// IsStorm 是节奏风暴代发的
	public sealed record Comment(DanmuSender Sender, string Text, bool IsStorm) : DanmakuEntryDto;

	// 缺少 Id 时为 0
	public sealed record SuperChat(long Id, DanmuSender Sender, string Text, long Price) : DanmakuEntryDto;

	// Ids 对应 SuperChat.Id；只用来标出被删的 SC，不单独列出
	public sealed record SuperChatDeletion(IReadOnlyList<long> Ids) : DanmakuEntryDto
	{
		// 按内容比较，和别的条目一样能直接判断相等
		public bool Equals(SuperChatDeletion? other)
		{
			return other is not null && base.Equals(other) && Ids.SequenceEqual(other.Ids);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(base.GetHashCode(), Ids.Count);
		}
	}

	// Price 是这一条的总价，银瓜子礼物为 0
	public sealed record Gift(DanmuSender Sender, string Name, int Count, long Price) : DanmakuEntryDto;

	// Price 是这一条实付的总价
	public sealed record Guard(DanmuSender Sender, GuardLevel Level, int Months, long Price, bool IsRenewal) : DanmakuEntryDto;

	// 以下只出现在弹幕文件里
	public sealed record TitleChange(string Title) : DanmakuEntryDto;

	// 弹幕连接断开到 End 为止，条目的时间是开始
	public sealed record Gap(TimeSpan End) : DanmakuEntryDto;

	public sealed record Connection(bool IsAnonymous, bool IsMasked) : DanmakuEntryDto;
}

public sealed record LiveDanmakuEntryDto(long Sequence, DateTimeOffset ReceivedAt, DanmakuEntryDto Entry);

// Time 是文件内时间，已含文件的时间偏移；文件里的发送者只有 uid 和昵称，舰队等级和粉丝牌为空
public sealed record DanmakuFileEntryDto(TimeSpan Time, DanmakuEntryDto Entry);
