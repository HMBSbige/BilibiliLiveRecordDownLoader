namespace BilibiliLive.Danmu;

public closed record DanmuMessage
{
	/// <summary>When the WebSocket frame that carried the message finished arriving; every message of one frame shares it.</summary>
	public DanmuReceiveTime ReceivedAt { get; init; }

	public sealed record LiveStarted : DanmuMessage;

	public sealed record LiveStopped(bool IsRound) : DanmuMessage;

	public sealed record RoomChanged(string Title, string? AreaName, string? ParentAreaName) : DanmuMessage;

	/// <summary>The server accepted the authentication; it precedes every other message of the connection.</summary>
	public sealed record Connected(long UserId) : DanmuMessage
	{
		public bool IsAnonymous => UserId is 0;
	}

	/// <summary>The server announced that this anonymous connection receives masked nicknames and uid 0.</summary>
	public sealed record LoginNotice(string Message) : DanmuMessage;

	/// <param name="Mode">1–3 scroll, 4 bottom, 5 top, as in the Bilibili danmaku XML.</param>
	/// <param name="Color">Decimal RGB.</param>
	/// <param name="UserHash">CRC32 of the sender's decimal uid in lowercase hexadecimal, sent even when the uid is masked, empty when absent.</param>
	/// <param name="Text">For an emoticon, its name.</param>
	/// <param name="IsStorm">Sent by a "rhythm storm", a phrase many viewers repeat with one click.</param>
	public sealed record Comment(DanmuSender Sender, string Text, int Mode, int FontSize, int Color, DateTimeOffset SentAt, string UserHash, DanmuCommentKind Kind, bool IsStorm) : DanmuMessage;

	/// <param name="Price">In thousandths of a yuan.</param>
	/// <param name="Duration">How long the room pins it.</param>
	public sealed record SuperChat(long Id, DanmuSender Sender, string Message, long Price, TimeSpan Duration, DateTimeOffset StartTime) : DanmuMessage;

	/// <summary>One kind of gift sent by one user, a protobuf gift message listing several gifts is split into one message each.</summary>
	/// <param name="UnitPrice">In units of <paramref name="CoinType"/>: a gold coin is a thousandth of a yuan, silver coins are not money.</param>
	/// <param name="TotalPrice">Price of all <paramref name="Count"/> gifts, in units of <paramref name="CoinType"/>.</param>
	public sealed record Gift(DanmuSender Sender, long GiftId, string GiftName, int Count, long UnitPrice, long TotalPrice, DanmuCoinType CoinType, DateTimeOffset Time) : DanmuMessage
	{
		/// <summary>In thousandths of a yuan.</summary>
		public long PaidPrice => CoinType is DanmuCoinType.Gold ? TotalPrice : 0;
	}

	/// <summary>The room removed these super chats from its list, they are the <see cref="SuperChat.Id"/> of earlier messages.</summary>
	public sealed record SuperChatDeleted(IReadOnlyList<long> Ids) : DanmuMessage;

	/// <param name="Count">Number of months.</param>
	/// <param name="Price">What was paid for all months, in thousandths of a yuan, discounts included.</param>
	/// <param name="IsRenewal">Extends a guard the user already had instead of starting one.</param>
	public sealed record GuardBought(DanmuSender Sender, GuardLevel Level, int Count, long Price, bool IsRenewal, DateTimeOffset StartTime) : DanmuMessage;

	/// <summary>Any command as received, emitted before the messages parsed from it.</summary>
	/// <param name="Command">The <c>cmd</c> without the <c>:</c> suffix some commands carry.</param>
	/// <param name="Json">The UTF-8 JSON of the whole packet body.</param>
	public sealed record Raw(string Command, ReadOnlyMemory<byte> Json) : DanmuMessage;
}

/// <param name="Timestamp">From <see cref="TimeProvider.GetTimestamp"/> of the api client's time provider.</param>
public readonly record struct DanmuReceiveTime(long Timestamp, DateTimeOffset UtcTime);

/// <param name="UserId">0 when an anonymous connection receives the message masked.</param>
public sealed record DanmuSender(long UserId, string Name, GuardLevel GuardLevel, DanmuMedal? Medal);

/// <param name="Color">Decimal RGB.</param>
/// <param name="RoomId">0 when the message does not carry it.</param>
public sealed record DanmuMedal(int Level, string Name, int Color, long StreamerUserId, long RoomId, bool IsLit);

public enum GuardLevel
{
	None = 0,
	Governor = 1,
	Admiral = 2,
	Captain = 3
}

public enum DanmuCommentKind
{
	Text = 0,
	Emoticon = 1,
	Voice = 2
}

public enum DanmuCoinType
{
	Silver,
	Gold
}
