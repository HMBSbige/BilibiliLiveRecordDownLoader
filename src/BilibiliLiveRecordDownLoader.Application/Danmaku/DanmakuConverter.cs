namespace BilibiliLiveRecordDownLoader.Application.Danmaku;

internal static class DanmakuConverter
{
	public static DanmakuRecord? ToRecord(DanmuMessage message, TimeSpan time, DanmakuProfile profile)
	{
		return message switch
		{
			DanmuMessage.Comment comment => new DanmakuRecord.Comment(time, comment.Mode, comment.FontSize, comment.Color, comment.SentAt, comment.Sender.UserId, comment.Sender.Name, comment.Text, comment.Id) { UidHash = comment.UserHash, IsStorm = comment.IsStorm },
			DanmuMessage.SuperChat superChat when profile.RecordSuperChats => new DanmakuRecord.SuperChat(time, superChat.Sender.UserId, superChat.Sender.Name, superChat.Price, superChat.Duration, superChat.Message) { Id = superChat.Id },
			DanmuMessage.SuperChatDeleted deleted when profile.RecordSuperChats => new DanmakuRecord.SuperChatDeletion(time, deleted.Ids),
			DanmuMessage.Gift gift when profile.GiftMode is DanmakuGiftMode.All || (profile.GiftMode is DanmakuGiftMode.Paid && gift.PaidPrice > 0) => new DanmakuRecord.Gift(time, gift.Sender.UserId, gift.Sender.Name, gift.GiftName, gift.Count, gift.PaidPrice),
			DanmuMessage.GuardBought guard when profile.RecordGuards => new DanmakuRecord.Guard(time, guard.Sender.UserId, guard.Sender.Name, (int)guard.Level, guard.Count, guard.Price) { IsRenewal = guard.IsRenewal },
			_ => null
		};
	}

	public static DanmakuEntryDto? ToEntry(DanmuMessage message)
	{
		return message switch
		{
			DanmuMessage.Comment comment => new DanmakuEntryDto.Comment(comment.Sender, comment.Text, comment.Color, comment.IsStorm),
			DanmuMessage.SuperChat superChat => new DanmakuEntryDto.SuperChat(superChat.Id, superChat.Sender, superChat.Message, superChat.Price, superChat.Duration),
			DanmuMessage.SuperChatDeleted deleted => new DanmakuEntryDto.SuperChatDeletion(deleted.Ids),
			DanmuMessage.Gift gift => new DanmakuEntryDto.Gift(gift.Sender, gift.GiftName, gift.Count, gift.PaidPrice),
			DanmuMessage.GuardBought guard => new DanmakuEntryDto.Guard(guard.Sender, guard.Level, guard.Count, guard.Price, guard.IsRenewal),
			_ => null
		};
	}

	public static DanmakuFileEntryDto ToEntry(DanmakuRecord record)
	{
		DanmakuEntryDto entry = record switch
		{
			DanmakuRecord.Comment comment => new DanmakuEntryDto.Comment(Sender(comment.Uid, comment.User), comment.Text, comment.Color, comment.IsStorm),
			DanmakuRecord.SuperChat superChat => new DanmakuEntryDto.SuperChat(superChat.Id, Sender(superChat.Uid, superChat.User), superChat.Text, superChat.Price, superChat.Duration),
			DanmakuRecord.SuperChatDeletion deletion => new DanmakuEntryDto.SuperChatDeletion(deletion.Ids),
			DanmakuRecord.Gift gift => new DanmakuEntryDto.Gift(Sender(gift.Uid, gift.User), gift.Name, gift.Count, gift.Price),
			DanmakuRecord.Guard guard => new DanmakuEntryDto.Guard(Sender(guard.Uid, guard.User), Enum.IsDefined((GuardLevel)guard.Level) ? (GuardLevel)guard.Level : GuardLevel.None, guard.Count, guard.Price, guard.IsRenewal),
			DanmakuRecord.TitleChange title => new DanmakuEntryDto.TitleChange(title.Title),
			DanmakuRecord.Gap gap => new DanmakuEntryDto.Gap(gap.End),
			DanmakuRecord.Connection connection => new DanmakuEntryDto.Connection(connection.Anonymous, connection.Masked)
		};

		return new DanmakuFileEntryDto(record.Time, entry);
	}

	public static DanmakuFileHeaderDto ToDto(DanmakuFileHeader header)
	{
		return new DanmakuFileHeaderDto(header.RoomId, header.ShortId, header.Streamer, header.Title, header.Area, header.ParentArea, header.LiveId, header.LiveStartTime, header.RecordStartTime, header.Offset, header.Version);
	}

	public static DanmakuHighlightDto ToDto(DanmakuHighlight highlight)
	{
		return new DanmakuHighlightDto(highlight.Start, highlight.Peak, highlight.End, highlight.Score, highlight.Phrase);
	}

	private static DanmuSender Sender(long uid, string name)
	{
		return new DanmuSender(uid, name, GuardLevel.None, null);
	}
}
