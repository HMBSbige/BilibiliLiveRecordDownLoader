using BilibiliLive.Danmu;
using BilibiliLiveRecordDownLoader.Application.Contracts.Danmaku;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using BilibiliLiveRecordDownLoader.ViewModels.Danmaku;
using System.Globalization;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class DanmakuEntryViewModelTest
{
	private static readonly DanmuSender Viewer = new(1, "观众", GuardLevel.None, null);

	private static DanmakuEntryViewModel File(DanmakuEntryDto entry, double seconds = 0)
	{
		return DanmakuEntryViewModel.FromFile(new DanmakuFileEntryDto(TimeSpan.FromSeconds(seconds), entry));
	}

	[Test]
	[Arguments(30_000L, "30")]
	[Arguments(1_234L, "1.23")]
	public async Task PriceInThousandthsOfAYuanIsShownInYuanAsync(long price, string yuan)
	{
		string expected = decimal.Parse(yuan, CultureInfo.InvariantCulture).ToString(CultureInfo.CurrentCulture);

		await Assert.That(DanmakuEntryViewModel.FormatPrice(price)).IsEqualTo(AppLanguage.Current.DanmakuPrice.ToString(expected));
	}

	public static IEnumerable<Func<(DanmakuEntryDto Entry, long? Price)>> Prices()
	{
		yield return static () => (new DanmakuEntryDto.SuperChat(1, Viewer, "加油", 30_000), 30_000);
		yield return static () => (new DanmakuEntryDto.Gift(Viewer, "小花花", 2, 200), 200);
		yield return static () => (new DanmakuEntryDto.Gift(Viewer, "辣条", 10, 0), null);
		yield return static () => (new DanmakuEntryDto.Guard(Viewer, GuardLevel.Captain, 1, 138_000, false), 138_000);
		yield return static () => (new DanmakuEntryDto.Guard(Viewer, GuardLevel.Captain, 1, 0, false), null);
		yield return static () => (Comment(), null);
	}

	[Test]
	[MethodDataSource(nameof(Prices))]
	public async Task OnlyPaidMessagesHaveAPriceAsync(DanmakuEntryDto entry, long? price)
	{
		await Assert.That(File(entry).PriceText).IsEqualTo(price is { } value ? DanmakuEntryViewModel.FormatPrice(value) : null);
	}

	public static IEnumerable<Func<(DanmakuEntryDto Entry, string Text)>> Texts()
	{
		yield return static () => (new DanmakuEntryDto.Gift(Viewer, "辣条", 10, 0), AppLanguage.Current.DanmakuGiftText.ToString("辣条", "10"));
		yield return static () => (new DanmakuEntryDto.Guard(Viewer, GuardLevel.Captain, 3, 414_000, true), AppLanguage.Current.DanmakuGuardRenewalMonthsText.ToString(AppLanguage.Current.GuardLevelCaptain, "3"));
		yield return static () => (new DanmakuEntryDto.Gap(TimeSpan.FromSeconds(105)), AppLanguage.Current.DanmakuGapText.ToString("0:01:45"));
		yield return static () => (new DanmakuEntryDto.Connection(false, false), AppLanguage.Current.DanmakuConnectionText.ToString(AppLanguage.Current.DanmakuLoggedIn));
		yield return static () => (new DanmakuEntryDto.Connection(true, true), AppLanguage.Current.DanmakuConnectionText.ToString(AppLanguage.Current.DanmakuAnonymousMasked));
		yield return static () => (new DanmakuEntryDto.Connection(true, false), AppLanguage.Current.DanmakuConnectionText.ToString(AppLanguage.Current.DanmakuAnonymous));
	}

	[Test]
	[MethodDataSource(nameof(Texts))]
	public async Task GiftsGuardsAndNoticesAreDescribedInTheCurrentLanguageAsync(DanmakuEntryDto entry, string text)
	{
		await Assert.That(File(entry).Text).IsEqualTo(text);
	}

	[Test]
	public async Task GuardBadgeTakesPrecedenceOverTheMedalAsync()
	{
		DanmuMedal medal = new(21, "粉丝团", 0x1A2B3C, true);
		DanmakuEntryViewModel guard = File(new DanmakuEntryDto.Comment(new DanmuSender(1, "舰长观众", GuardLevel.Captain, medal), "hi", false));
		DanmakuEntryViewModel fan = File(new DanmakuEntryDto.Comment(new DanmuSender(1, "粉丝", GuardLevel.None, medal), "hi", false));
		DanmakuEntryViewModel unlit = File(new DanmakuEntryDto.Comment(new DanmuSender(1, "粉丝", GuardLevel.None, medal with { IsLit = false }), "hi", false));

		using (Assert.Multiple())
		{
			await Assert.That((guard.Badge, guard.IsGuardBadge, guard.MedalColor, guard.IsUnlitMedal)).IsEqualTo((AppLanguage.Current.GuardLevelCaptain, true, null, false));
			await Assert.That((fan.Badge, fan.IsGuardBadge, fan.MedalColor, fan.IsUnlitMedal)).IsEqualTo(("粉丝团 21", false, 0x1A2B3C, false));
			await Assert.That((unlit.MedalColor, unlit.IsUnlitMedal)).IsEqualTo((null, true));
		}
	}

	[Test]
	public async Task AccessibleTextReadsTheWholeRowAsync()
	{
		DanmuMedal medal = new(21, "粉丝团", 0x1A2B3C, true);
		DanmakuEntryViewModel comment = File(new DanmakuEntryDto.Comment(new DanmuSender(1, "粉丝", GuardLevel.None, medal), "你好", false), 65);
		DanmakuEntryViewModel superChat = File(new DanmakuEntryDto.SuperChat(7, Viewer, "加油", 30_000));
		DanmakuEntryViewModel title = File(new DanmakuEntryDto.TitleChange("新标题"));
		bool notified = false;
		superChat.PropertyChanged += (_, e) => notified |= e.PropertyName is nameof(DanmakuEntryViewModel.AccessibleText);

		DanmakuEntryViewModel.MarkDeleted([superChat], [7]);

		using (Assert.Multiple())
		{
			await Assert.That(comment.AccessibleText).IsEqualTo($"0:01:05 粉丝团 21 粉丝{AppLanguage.Current.DanmakuNameSeparator}你好");
			await Assert.That(superChat.AccessibleText).IsEqualTo($"0:00:00 观众{AppLanguage.Current.DanmakuNameSeparator}加油 {DanmakuEntryViewModel.FormatPrice(30_000)} {AppLanguage.Current.DanmakuSuperChatDeleted}");
			await Assert.That(notified).IsTrue();
			await Assert.That(title.AccessibleText).IsEqualTo($"0:00:00 {AppLanguage.Current.DanmakuTitleText.ToString("新标题")}");
		}
	}

	[Test]
	public async Task SearchIgnoresCaseInNamesAndTextAsync()
	{
		DanmakuEntryViewModel entry = File(new DanmakuEntryDto.Comment(new DanmuSender(1, "Viewer", GuardLevel.None, null), "Hello 世界", false));

		using (Assert.Multiple())
		{
			await Assert.That(entry.Contains("hello")).IsTrue();
			await Assert.That(entry.Contains("VIEW")).IsTrue();
			await Assert.That(entry.Contains("世界")).IsTrue();
			await Assert.That(entry.Contains("再见")).IsFalse();
		}
	}

	[Test]
	public async Task OnlySuperChatsWithAMatchingIdAreMarkedDeletedAsync()
	{
		DanmakuEntryViewModel deleted = File(new DanmakuEntryDto.SuperChat(7, Viewer, "被删", 30_000));
		DanmakuEntryViewModel kept = File(new DanmakuEntryDto.SuperChat(8, Viewer, "留着", 30_000));
		DanmakuEntryViewModel withoutId = File(new DanmakuEntryDto.SuperChat(0, Viewer, "旧文件", 30_000));

		DanmakuEntryViewModel.MarkDeleted([deleted, kept, withoutId], [7, 0]);

		await Assert.That((deleted.IsDeleted, kept.IsDeleted, withoutId.IsDeleted)).IsEqualTo((true, false, false));
	}

	[Test]
	[Arguments(3 * 3600 + 2 * 60 + 1d, "3:02:01")]
	[Arguments(-1.5, "0:00:00")]
	public async Task FileEntriesShowTheirTimeInTheFileAsync(double seconds, string text)
	{
		DanmakuEntryViewModel entry = File(Comment(), seconds);

		await Assert.That((entry.TimeText, entry.Time)).IsEqualTo((text, (TimeSpan?)TimeSpan.FromSeconds(seconds)));
	}

	private static DanmakuEntryDto.Comment Comment()
	{
		return new DanmakuEntryDto.Comment(Viewer, "hi", false);
	}
}
