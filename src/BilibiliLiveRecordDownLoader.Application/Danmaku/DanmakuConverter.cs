namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

internal static class DanmakuConverter
{
	public static DanmakuRecord? ToRecord(DanmuMessage message, TimeSpan time, DanmakuProfile profile)
	{
		return message switch
		{
			DanmuMessage.Comment comment => new DanmakuRecord.Comment(time, comment.Mode, comment.FontSize, comment.Color, comment.SentAt, comment.Sender.UserId, comment.Sender.Name, comment.Text, 0) { UidHash = comment.UserHash, IsStorm = comment.IsStorm },
			DanmuMessage.SuperChat superChat => profile.RecordSuperChats ? new DanmakuRecord.SuperChat(time, superChat.Sender.UserId, superChat.Sender.Name, superChat.Price, superChat.Duration, superChat.Message) { Id = superChat.Id } : null,
			DanmuMessage.SuperChatDeleted deleted => profile.RecordSuperChats ? new DanmakuRecord.SuperChatDeletion(time, deleted.Ids) : null,
			DanmuMessage.Gift gift => (profile.GiftMode, gift.PaidPrice) is (DanmakuGiftMode.All, _) or (DanmakuGiftMode.Paid, > 0) ? new DanmakuRecord.Gift(time, gift.Sender.UserId, gift.Sender.Name, gift.GiftName, gift.Count, gift.PaidPrice) : null,
			DanmuMessage.GuardBought guard => profile.RecordGuards ? new DanmakuRecord.Guard(time, guard.Sender.UserId, guard.Sender.Name, guard.Level, guard.Count, guard.Price) { IsRenewal = guard.IsRenewal } : null,
			DanmuMessage.LiveStarted or DanmuMessage.LiveStopped or DanmuMessage.RoomChanged or DanmuMessage.Connected or DanmuMessage.LoginNotice or DanmuMessage.Raw => null
		};
	}

	public static DanmakuEntryDto? ToEntry(DanmuMessage message)
	{
		return message switch
		{
			DanmuMessage.Comment comment => new DanmakuEntryDto.Comment(comment.Sender, comment.Text, comment.IsStorm),
			DanmuMessage.SuperChat superChat => new DanmakuEntryDto.SuperChat(superChat.Id, superChat.Sender, superChat.Message, superChat.Price),
			DanmuMessage.SuperChatDeleted deleted => new DanmakuEntryDto.SuperChatDeletion(deleted.Ids),
			DanmuMessage.Gift gift => new DanmakuEntryDto.Gift(gift.Sender, gift.GiftName, gift.Count, gift.PaidPrice),
			DanmuMessage.GuardBought guard => new DanmakuEntryDto.Guard(guard.Sender, guard.Level, guard.Count, guard.Price, guard.IsRenewal),
			DanmuMessage.LiveStarted or DanmuMessage.LiveStopped or DanmuMessage.RoomChanged or DanmuMessage.Connected or DanmuMessage.LoginNotice or DanmuMessage.Raw => null
		};
	}

	public static DanmakuFileEntryDto ToFileEntry(DanmakuRecord record)
	{
		DanmakuEntryDto entry = record switch
		{
			DanmakuRecord.Comment comment => new DanmakuEntryDto.Comment(Sender(comment.Uid, comment.User), comment.Text, comment.IsStorm),
			DanmakuRecord.SuperChat superChat => new DanmakuEntryDto.SuperChat(superChat.Id, Sender(superChat.Uid, superChat.User), superChat.Text, superChat.Price),
			DanmakuRecord.SuperChatDeletion deletion => new DanmakuEntryDto.SuperChatDeletion(deletion.Ids),
			DanmakuRecord.Gift gift => new DanmakuEntryDto.Gift(Sender(gift.Uid, gift.User), gift.Name, gift.Count, gift.Price),
			DanmakuRecord.Guard guard => new DanmakuEntryDto.Guard(Sender(guard.Uid, guard.User), guard.Level, guard.Count, guard.Price, guard.IsRenewal),
			DanmakuRecord.TitleChange title => new DanmakuEntryDto.TitleChange(title.Title),
			DanmakuRecord.Gap gap => new DanmakuEntryDto.Gap(gap.End),
			DanmakuRecord.Connection connection => new DanmakuEntryDto.Connection(connection.Anonymous, connection.Masked)
		};

		return new DanmakuFileEntryDto(record.Time, entry);
	}

	public static DanmakuHighlightDto ToDto(DanmakuHighlight highlight)
	{
		return new DanmakuHighlightDto(highlight.Start, highlight.Peak, highlight.End, highlight.Phrase);
	}

	private static DanmuSender Sender(long uid, string name)
	{
		return new DanmuSender(uid, name, GuardLevel.None, null);
	}
}
