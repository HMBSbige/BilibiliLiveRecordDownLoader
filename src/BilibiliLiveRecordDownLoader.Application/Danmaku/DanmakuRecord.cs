namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

// Time 是文件内时间，以文件第一帧为 0；写入时再加上文件头里的时间偏移
internal closed record DanmakuRecord(TimeSpan Time)
{
	// Mode、FontSize、Color 沿用 B 站弹幕的取值，Color 是 RGB
	// DmId 是 B 站标准文件里的十进制 dmid，改写偏移时原样写回；直播弹幕没有，为 0
	public sealed record Comment(TimeSpan Time, int Mode, int FontSize, int Color, DateTimeOffset SentAt, long Uid, string User, string Text, long DmId) : DanmakuRecord(Time)
	{
		// 打码时 uid 为 0，但消息里仍带真实 uid 的哈希
		// 构造时就算好存下，按 uid 算出的和读回的同一个哈希才会相等
		public string UidHash
		{
			get;
			init => field = string.IsNullOrEmpty(value) ? DanmakuUid.Hash(Uid) : value;
		} = DanmakuUid.Hash(Uid);

		// 节奏风暴是系统替观众代发的，高能检测要排除
		public bool IsStorm { get; init; }
	}

	// Price 是千分之一元
	public sealed record SuperChat(TimeSpan Time, long Uid, string User, long Price, TimeSpan Duration, string Text) : DanmakuRecord(Time)
	{
		// 缺少 id 时为 0
		public long Id { get; init; }
	}

	public sealed record SuperChatDeletion(TimeSpan Time, IReadOnlyList<long> Ids) : DanmakuRecord(Time)
	{
		// 按内容比较，读回的记录才和写入的相等
		public bool Equals(SuperChatDeletion? other)
		{
			return other is not null && base.Equals(other) && Ids.SequenceEqual(other.Ids);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(base.GetHashCode(), Ids.Count);
		}
	}

	// Price 是这一条的总价，千分之一元；银瓜子礼物为 0
	public sealed record Gift(TimeSpan Time, long Uid, string User, string Name, int Count, long Price) : DanmakuRecord(Time);

	// Count 是月数；Price 是这一条实付的总价，千分之一元
	public sealed record Guard(TimeSpan Time, long Uid, string User, GuardLevel Level, int Count, long Price) : DanmakuRecord(Time)
	{
		public bool IsRenewal { get; init; }
	}

	public sealed record TitleChange(TimeSpan Time, string Title) : DanmakuRecord(Time);

	// 弹幕连接断开的区间，Time 是开始
	public sealed record Gap(TimeSpan Time, TimeSpan End) : DanmakuRecord(Time);

	// Masked 表示收到了未登录看不到昵称的通知
	public sealed record Connection(TimeSpan Time, bool Anonymous, bool Masked) : DanmakuRecord(Time);

	public DanmakuRecord Shift(TimeSpan delta)
	{
		return this switch
		{
			Gap gap => gap with { Time = gap.Time + delta, End = gap.End + delta },
			Comment or SuperChat or SuperChatDeletion or Gift or Guard or TitleChange or Connection => this with { Time = Time + delta }
		};
	}
}
