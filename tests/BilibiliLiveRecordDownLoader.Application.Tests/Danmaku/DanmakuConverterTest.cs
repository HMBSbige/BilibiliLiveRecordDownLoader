using BilibiliLive.Danmu;
using BilibiliLiveRecordDownLoader.Application.Contracts.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Danmaku;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Danmaku;

public sealed class DanmakuConverterTest
{
	private static readonly DanmuSender Sender = new(42, "观众", GuardLevel.Captain, new DanmuMedal(21, "牌子", 0x1A544B, 1, 2, true));

	private static readonly TimeSpan Time = TimeSpan.FromSeconds(12.5);

	private static DanmakuProfile Profile(bool superChats = true, DanmakuGiftMode gifts = DanmakuGiftMode.None, bool guards = true)
	{
		return new DanmakuProfile(true, superChats, gifts, guards, TimeSpan.Zero, true, true, false, false);
	}

	private static DanmuMessage.Gift Gift(DanmuCoinType coinType)
	{
		return new DanmuMessage.Gift(Sender, 1, "小花花", 2, 100, 200, coinType, DateTimeOffset.UnixEpoch);
	}

	[Test]
	public async Task CommentKeepsTheUidHashAndStormFlagAsync()
	{
		DanmuMessage.Comment comment = new(Sender with { UserId = 0, Name = "观***" }, "文本", 5, 36, 0xFF0000, DateTimeOffset.FromUnixTimeSeconds(1795343190), "a16fe0dd", DanmuCommentKind.Text, true);

		DanmakuRecord? record = DanmakuConverter.ToRecord(comment, Time, Profile(superChats: false, guards: false));

		await Assert.That(record).IsEqualTo(new DanmakuRecord.Comment(Time, 5, 36, 0xFF0000, comment.SentAt, 0, "观***", "文本", 0) { UidHash = "a16fe0dd", IsStorm = true });
	}

	[Test]
	[Arguments(DanmakuGiftMode.None, DanmuCoinType.Gold, false)]
	[Arguments(DanmakuGiftMode.Paid, DanmuCoinType.Gold, true)]
	[Arguments(DanmakuGiftMode.Paid, DanmuCoinType.Silver, false)]
	public async Task GiftsFollowTheGiftModeAsync(DanmakuGiftMode mode, DanmuCoinType coinType, bool isRecorded)
	{
		await Assert.That(DanmakuConverter.ToRecord(Gift(coinType), Time, Profile(gifts: mode)) is not null).IsEqualTo(isRecorded);
	}

	[Test]
	public async Task GiftPriceIsTheTotalAndSilverGiftsAreFreeAsync()
	{
		using (Assert.Multiple())
		{
			await Assert.That(DanmakuConverter.ToRecord(Gift(DanmuCoinType.Gold), Time, Profile(gifts: DanmakuGiftMode.All))).IsEqualTo(new DanmakuRecord.Gift(Time, 42, "观众", "小花花", 2, 200));
			await Assert.That(DanmakuConverter.ToRecord(Gift(DanmuCoinType.Silver), Time, Profile(gifts: DanmakuGiftMode.All))).IsEqualTo(new DanmakuRecord.Gift(Time, 42, "观众", "小花花", 2, 0));
		}
	}

	[Test]
	public async Task GuardKeepsThePaidPriceAndRenewalAsync()
	{
		DanmuMessage.GuardBought guard = new(Sender, GuardLevel.Captain, 3, 414000, true, DateTimeOffset.UnixEpoch);

		using (Assert.Multiple())
		{
			await Assert.That(DanmakuConverter.ToRecord(guard, Time, Profile())).IsEqualTo(new DanmakuRecord.Guard(Time, 42, "观众", GuardLevel.Captain, 3, 414000) { IsRenewal = true });
			await Assert.That(DanmakuConverter.ToRecord(guard, Time, Profile(guards: false))).IsNull();
		}
	}

	[Test]
	public async Task SuperChatsFollowTheSettingAsync()
	{
		DanmuMessage.SuperChat superChat = new(7, Sender, "醒目留言", 30000, TimeSpan.FromSeconds(60), DateTimeOffset.UnixEpoch);

		using (Assert.Multiple())
		{
			await Assert.That(DanmakuConverter.ToRecord(superChat, Time, Profile())).IsEqualTo(new DanmakuRecord.SuperChat(Time, 42, "观众", 30000, TimeSpan.FromSeconds(60), "醒目留言") { Id = 7 });
			await Assert.That(DanmakuConverter.ToRecord(superChat, Time, Profile(superChats: false))).IsNull();
		}
	}

	// 没记 SC 时删除也没有可对应的
	[Test]
	public async Task SuperChatDeletionsFollowTheSuperChatSettingAsync()
	{
		DanmuMessage.SuperChatDeleted deleted = new([7, 8]);

		using (Assert.Multiple())
		{
			await Assert.That(DanmakuConverter.ToRecord(deleted, Time, Profile(gifts: DanmakuGiftMode.All))).IsEqualTo(new DanmakuRecord.SuperChatDeletion(Time, [7, 8]));
			await Assert.That(DanmakuConverter.ToRecord(deleted, Time, Profile(superChats: false, gifts: DanmakuGiftMode.All))).IsNull();
			await Assert.That(DanmakuConverter.ToEntry(deleted)).IsEqualTo(new DanmakuEntryDto.SuperChatDeletion([7, 8]));
		}
	}

	[Test]
	public async Task FeedEntriesCarryEverythingTheListShowsAsync()
	{
		using (Assert.Multiple())
		{
			await Assert.That(DanmakuConverter.ToEntry(Gift(DanmuCoinType.Gold))).IsEqualTo(new DanmakuEntryDto.Gift(Sender, "小花花", 2, 200));
			await Assert.That(DanmakuConverter.ToEntry(new DanmuMessage.GuardBought(Sender, GuardLevel.Admiral, 2, 3996000, true, DateTimeOffset.UnixEpoch))).IsEqualTo(new DanmakuEntryDto.Guard(Sender, GuardLevel.Admiral, 2, 3996000, true));
			await Assert.That(DanmakuConverter.ToEntry(new DanmuMessage.SuperChat(7, Sender, "醒目留言", 30000, TimeSpan.FromSeconds(60), DateTimeOffset.UnixEpoch))).IsEqualTo(new DanmakuEntryDto.SuperChat(7, Sender, "醒目留言", 30000, TimeSpan.FromSeconds(60)));
			await Assert.That(DanmakuConverter.ToEntry(new DanmuMessage.LiveStarted())).IsNull();
		}
	}

	[Test]
	public async Task FileEntriesComeFromEveryKindOfRecordAsync()
	{
		await Assert.That(DanmakuSamples.AllKinds().Select(DanmakuConverter.ToEntry)).IsEquivalentTo(
		[
			new(TimeSpan.Zero, new DanmakuEntryDto.Connection(true, true)),
			new(TimeSpan.FromMilliseconds(12345), new DanmakuEntryDto.Comment(FileSender(123, "昵称"), "文本", 16777215, false)),
			new(TimeSpan.FromMilliseconds(13000), new DanmakuEntryDto.Comment(FileSender(0, "淡***"), "节奏风暴", 0xFF0000, true)),
			new(TimeSpan.FromSeconds(20), new DanmakuEntryDto.SuperChat(20541912, FileSender(456, "老板"), "醒目留言", 30000, TimeSpan.FromSeconds(60))),
			new(TimeSpan.FromMilliseconds(21500), new DanmakuEntryDto.Gift(FileSender(789, "观众"), "小花花", 10, 1000)),
			new(TimeSpan.FromSeconds(30), new DanmakuEntryDto.Guard(FileSender(1011, "舰长"), GuardLevel.Captain, 1, 198000, false)),
			new(TimeSpan.FromSeconds(40), new DanmakuEntryDto.TitleChange("新标题")),
			new(TimeSpan.FromSeconds(100), new DanmakuEntryDto.Gap(TimeSpan.FromMilliseconds(105200))),
			new(TimeSpan.FromMilliseconds(105200), new DanmakuEntryDto.Connection(false, false)),
			new(TimeSpan.FromSeconds(110), new DanmakuEntryDto.SuperChatDeletion([20541912, 20541913])),
			new(TimeSpan.FromSeconds(120), new DanmakuEntryDto.Guard(FileSender(1012, "老舰长"), GuardLevel.Admiral, 3, 4_194_000, true))
		], EqualityComparer<DanmakuFileEntryDto>.Default, CollectionOrdering.Matching);
	}

	// 文件里的发送者只有 uid 和昵称
	private static DanmuSender FileSender(long uid, string name)
	{
		return new DanmuSender(uid, name, GuardLevel.None, null);
	}
}
