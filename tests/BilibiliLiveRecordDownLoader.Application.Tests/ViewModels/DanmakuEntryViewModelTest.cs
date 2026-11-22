using BilibiliLive.Danmu;
using BilibiliLiveRecordDownLoader.Application.Contracts.Danmaku;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using BilibiliLiveRecordDownLoader.ViewModels.Danmaku;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class DanmakuEntryViewModelTest
{
	private static readonly DanmuSender Viewer = new(1, "观众", GuardLevel.None, null);

	private static DanmakuEntryViewModel File(DanmakuEntryDto entry, double seconds = 0)
	{
		return DanmakuEntryViewModel.FromFile(0, new DanmakuFileEntryDto(TimeSpan.FromSeconds(seconds), entry));
	}

	// 面板和字幕共用同一份档位配色
	[Test]
	[Arguments(30_000L)]
	[Arguments(100_000L)]
	[Arguments(2_500_000L)]
	public async Task SuperChatIsColoredByItsTierAsync(long price)
	{
		DanmakuEntryViewModel entry = File(new DanmakuEntryDto.SuperChat(1, Viewer, "加油", price, TimeSpan.FromMinutes(1)));
		SuperChatTier tier = SuperChatTier.For(price);

		using (Assert.Multiple())
		{
			await Assert.That(entry.IsSuperChat).IsTrue();
			await Assert.That((entry.SuperChatBackground, entry.SuperChatPriceColor, entry.SuperChatBottomBackground)).IsEqualTo((tier.Background, tier.PriceColor, tier.BottomBackground));
			await Assert.That(entry.PriceText).IsEqualTo(DanmakuEntryViewModel.FormatPrice(price));
		}
	}

	[Test]
	[Arguments(30_000L, "30")]
	[Arguments(500L, "0.5")]
	[Arguments(1_234L, "1.23")]
	public async Task PriceInThousandthsOfAYuanIsShownInYuanAsync(long price, string yuan)
	{
		await Assert.That(DanmakuEntryViewModel.FormatPrice(price)).IsEqualTo(AppLanguage.Current.DanmakuPrice.ToString(yuan));
	}

	// 银瓜子礼物不花钱
	[Test]
	public async Task FreeGiftsHaveNoPriceAsync()
	{
		DanmakuEntryViewModel free = File(new DanmakuEntryDto.Gift(Viewer, "辣条", 10, 0));
		DanmakuEntryViewModel paid = File(new DanmakuEntryDto.Gift(Viewer, "小花花", 2, 200));

		using (Assert.Multiple())
		{
			await Assert.That(free.PriceText).IsNull();
			await Assert.That(free.Text).IsEqualTo(AppLanguage.Current.DanmakuGiftText.ToString("辣条", "10"));
			await Assert.That(paid.PriceText).IsEqualTo(DanmakuEntryViewModel.FormatPrice(200));
		}
	}

	[Test]
	public async Task GuardBadgeTakesPrecedenceOverTheMedalAsync()
	{
		DanmuMedal medal = new(21, "粉丝团", 0x1A2B3C, 2, 3, true);
		DanmakuEntryViewModel guard = File(new DanmakuEntryDto.Comment(new DanmuSender(1, "舰长观众", GuardLevel.Captain, medal), "hi", 0xFFFFFF, false));
		DanmakuEntryViewModel fan = File(new DanmakuEntryDto.Comment(new DanmuSender(1, "粉丝", GuardLevel.None, medal), "hi", 0xFFFFFF, false));
		DanmakuEntryViewModel unlit = File(new DanmakuEntryDto.Comment(new DanmuSender(1, "粉丝", GuardLevel.None, medal with { IsLit = false }), "hi", 0xFFFFFF, false));

		using (Assert.Multiple())
		{
			await Assert.That((guard.Badge, guard.IsGuardBadge, guard.MedalColor)).IsEqualTo((AppLanguage.Current.GuardLevelCaptain, true, null));
			await Assert.That((fan.Badge, fan.IsGuardBadge, fan.MedalColor)).IsEqualTo(("粉丝团 21", false, 0x1A2B3C));
			await Assert.That(unlit.MedalColor).IsNull();
		}
	}

	[Test]
	public async Task GuardMonthsAreShownOnlyForMoreThanOneMonthAsync()
	{
		DanmakuEntryViewModel one = File(new DanmakuEntryDto.Guard(Viewer, GuardLevel.Admiral, 1, 1_998_000, false));
		DanmakuEntryViewModel three = File(new DanmakuEntryDto.Guard(Viewer, GuardLevel.Captain, 3, 594_000, false));

		using (Assert.Multiple())
		{
			await Assert.That(one.Text).IsEqualTo(AppLanguage.Current.DanmakuGuardText.ToString(AppLanguage.Current.GuardLevelAdmiral));
			await Assert.That(three.Text).IsEqualTo(AppLanguage.Current.DanmakuGuardMonthsText.ToString(AppLanguage.Current.GuardLevelCaptain, "3"));
		}
	}

	[Test]
	public async Task RenewalsAreToldApartFromNewGuardsAsync()
	{
		DanmakuEntryViewModel one = File(new DanmakuEntryDto.Guard(Viewer, GuardLevel.Captain, 1, 138_000, true));
		DanmakuEntryViewModel three = File(new DanmakuEntryDto.Guard(Viewer, GuardLevel.Captain, 3, 414_000, true));

		using (Assert.Multiple())
		{
			await Assert.That(one.Text).IsEqualTo(AppLanguage.Current.DanmakuGuardRenewalText.ToString(AppLanguage.Current.GuardLevelCaptain));
			await Assert.That(three.Text).IsEqualTo(AppLanguage.Current.DanmakuGuardRenewalMonthsText.ToString(AppLanguage.Current.GuardLevelCaptain, "3"));
			await Assert.That(one.Text).IsNotEqualTo(File(new DanmakuEntryDto.Guard(Viewer, GuardLevel.Captain, 1, 138_000, false)).Text);
		}
	}

	[Test]
	public async Task OnlySuperChatsWithAMatchingIdAreMarkedDeletedAsync()
	{
		DanmakuEntryViewModel deleted = File(new DanmakuEntryDto.SuperChat(7, Viewer, "被删", 30_000, TimeSpan.FromMinutes(1)));
		DanmakuEntryViewModel kept = File(new DanmakuEntryDto.SuperChat(8, Viewer, "留着", 30_000, TimeSpan.FromMinutes(1)));
		DanmakuEntryViewModel withoutId = File(new DanmakuEntryDto.SuperChat(0, Viewer, "旧文件", 30_000, TimeSpan.FromMinutes(1)));

		DanmakuEntryViewModel.MarkDeleted([deleted, kept, withoutId], [7, 0]);

		using (Assert.Multiple())
		{
			await Assert.That((deleted.IsDeleted, kept.IsDeleted, withoutId.IsDeleted)).IsEqualTo((true, false, false));
			await Assert.That(deleted.DeletedText).IsEqualTo(AppLanguage.Current.DanmakuSuperChatDeleted);
		}
	}

	[Test]
	public async Task FileEntriesShowTheirTimeInTheFileAsync()
	{
		DanmakuEntryViewModel early = File(FakeComment(), 65.4);
		DanmakuEntryViewModel late = File(FakeComment(), 3 * 3600 + 2 * 60 + 1);

		using (Assert.Multiple())
		{
			await Assert.That((early.TimeText, early.Time)).IsEqualTo(("0:01:05", (TimeSpan?)TimeSpan.FromSeconds(65.4)));
			await Assert.That(late.TimeText).IsEqualTo("3:02:01");
		}
	}

	[Test]
	public async Task NoticesFromTheFileHaveNoSenderAsync()
	{
		DanmakuEntryViewModel title = File(new DanmakuEntryDto.TitleChange("新标题"));
		DanmakuEntryViewModel gap = File(new DanmakuEntryDto.Gap(TimeSpan.FromSeconds(105)));
		DanmakuEntryViewModel connection = File(new DanmakuEntryDto.Connection(true, true));

		using (Assert.Multiple())
		{
			await Assert.That((title.Kind, title.Name, title.Badge)).IsEqualTo((DanmakuEntryKind.Notice, null, null));
			await Assert.That(title.Text).IsEqualTo(AppLanguage.Current.DanmakuTitleText.ToString("新标题"));
			await Assert.That(gap.Text).IsEqualTo(AppLanguage.Current.DanmakuGapText.ToString("0:01:45"));
			await Assert.That(connection.Text).IsEqualTo(AppLanguage.Current.DanmakuConnectionText.ToString(AppLanguage.Current.DanmakuAnonymousMasked));
		}
	}

	private static DanmakuEntryDto.Comment FakeComment()
	{
		return new DanmakuEntryDto.Comment(Viewer, "hi", 0xFFFFFF, false);
	}
}
