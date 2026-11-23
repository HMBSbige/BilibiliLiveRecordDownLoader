namespace BilibiliLive.Danmu;

public closed record DanmuMessage
{
	/// <summary>所在 WebSocket 帧收完的时间，同一帧的消息相同</summary>
	public DanmuReceiveTime ReceivedAt { get; init; }

	public sealed record LiveStarted : DanmuMessage;

	public sealed record LiveStopped(bool IsRound) : DanmuMessage;

	public sealed record RoomChanged(string Title, string? AreaName, string? ParentAreaName) : DanmuMessage;

	/// <summary>服务器接受了认证，先于这条连接的其他所有消息</summary>
	public sealed record Connected(long UserId) : DanmuMessage
	{
		public bool IsAnonymous => UserId is 0;
	}

	/// <summary>服务器通知匿名连接收到的昵称打码、uid 为 0</summary>
	public sealed record LoginNotice : DanmuMessage;

	/// <param name="Mode">1–3 滚动，4 底部，5 顶部，和 B 站弹幕 XML 一致</param>
	/// <param name="Color">十进制 RGB</param>
	/// <param name="UserHash">发送者十进制 uid 的 CRC32，小写十六进制；uid 打码时也会发，缺失时为空</param>
	/// <param name="Text">表情弹幕是表情的名称</param>
	public sealed record Comment(DanmuSender Sender, string Text, int Mode, int FontSize, int Color, DateTimeOffset SentAt, string UserHash, bool IsStorm) : DanmuMessage;

	/// <param name="Price">单位是千分之一元</param>
	public sealed record SuperChat(long Id, DanmuSender Sender, string Message, long Price, TimeSpan Duration) : DanmuMessage;

	/// <summary>一种礼物；列出多种礼物的 protobuf 礼物消息会拆成每种一条</summary>
	/// <param name="TotalPrice">全部 <paramref name="Count"/> 个礼物的价格，单位由 <paramref name="CoinType"/> 决定：金瓜子是千分之一元，银瓜子不是钱</param>
	public sealed record Gift(DanmuSender Sender, string GiftName, int Count, long TotalPrice, DanmuCoinType CoinType) : DanmuMessage
	{
		/// <summary>单位是千分之一元</summary>
		public long PaidPrice => CoinType is DanmuCoinType.Gold ? TotalPrice : 0;
	}

	public sealed record SuperChatDeleted(IReadOnlyList<long> Ids) : DanmuMessage;

	/// <param name="Count">月数</param>
	/// <param name="Price">所有月份的实付价格，计入了折扣，单位是千分之一元</param>
	public sealed record GuardBought(DanmuSender Sender, GuardLevel Level, int Count, long Price, bool IsRenewal) : DanmuMessage;

	/// <summary>收到的任意命令的原文，在从它解析出的消息之前产出</summary>
	/// <param name="Command">去掉了部分命令带的 <c>:</c> 后缀的 <c>cmd</c></param>
	/// <param name="Json">整个包体的 UTF-8 JSON</param>
	public sealed record Raw(string Command, ReadOnlyMemory<byte> Json) : DanmuMessage;
}

/// <param name="Timestamp">来自 API 客户端所用时间提供程序的 <see cref="TimeProvider.GetTimestamp"/></param>
public readonly record struct DanmuReceiveTime(long Timestamp, DateTimeOffset UtcTime);

/// <param name="UserId">匿名连接收到打码的消息时为 0</param>
public sealed record DanmuSender(long UserId, string Name, GuardLevel GuardLevel, DanmuMedal? Medal);

/// <param name="Color">十进制 RGB</param>
public sealed record DanmuMedal(int Level, string Name, int Color, bool IsLit);

public enum GuardLevel
{
	None = 0,
	Governor = 1,
	Admiral = 2,
	Captain = 3
}

public enum DanmuCoinType
{
	Silver,
	Gold
}
