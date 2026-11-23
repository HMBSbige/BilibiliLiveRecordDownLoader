namespace BilibiliLiveRecordDownLoader.Application.Contracts.Danmaku;

// 价格都是千分之一元
public closed record DanmakuEntryDto
{
	public sealed record Comment(DanmuSender Sender, string Text, bool IsStorm) : DanmakuEntryDto;

	// 缺少 Id 时为 0
	public sealed record SuperChat(long Id, DanmuSender Sender, string Text, long Price) : DanmakuEntryDto;

	public sealed record SuperChatDeletion(IReadOnlyList<long> Ids) : DanmakuEntryDto
	{
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

	public sealed record TitleChange(string Title) : DanmakuEntryDto;

	// 弹幕连接断开到 End 为止，条目的时间是开始
	public sealed record Gap(TimeSpan End) : DanmakuEntryDto;

	public sealed record Connection(bool IsAnonymous, bool IsMasked) : DanmakuEntryDto;
}

public sealed record LiveDanmakuEntryDto(long Sequence, DateTimeOffset ReceivedAt, DanmakuEntryDto Entry);

// Time 是文件内时间，已含文件的时间偏移
public sealed record DanmakuFileEntryDto(TimeSpan Time, DanmakuEntryDto Entry);
