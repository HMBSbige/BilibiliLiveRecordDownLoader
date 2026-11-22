using BilibiliLive.Danmu;
using BilibiliLiveRecordDownLoader.Application.Contracts.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using BilibiliLiveRecordDownLoader.ViewModels.Danmaku;
using System.Windows.Input;
using TUnit.Assertions.Enums;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class LiveDanmakuViewModelTest
{
	[Test]
	public async Task OnlyEntriesAfterTheLastOneShownAreAppendedAsync()
	{
		FakeDanmakuFeed feed = new(1);
		feed.Add(FakeDanmakuFeed.Comment("a"), FakeDanmakuFeed.Comment("b"));
		using DanmakuFilterViewModel filter = new();
		using LiveDanmakuViewModel viewModel = new(feed, filter, false);
		string[] initial = Texts(viewModel);

		feed.Add(FakeDanmakuFeed.Comment("c"));
		viewModel.Refresh();
		viewModel.Refresh();

		using (Assert.Multiple())
		{
			await Assert.That(initial).IsEquivalentTo(["a", "b"], EqualityComparer<string>.Default, CollectionOrdering.Matching);
			await Assert.That(Texts(viewModel)).IsEquivalentTo(["a", "b", "c"], EqualityComparer<string>.Default, CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task OnlyTheMostRecentEntriesAreKeptAsync()
	{
		FakeDanmakuFeed feed = new(1);
		using DanmakuFilterViewModel filter = new();
		using LiveDanmakuViewModel viewModel = new(feed, filter, false);

		for (int index = 0; index < DanmakuConsts.MaxFeedEntries + 5; ++index)
		{
			feed.Add(FakeDanmakuFeed.Comment(index.ToString(System.Globalization.CultureInfo.InvariantCulture)));

			// 像界面那样成批加入
			if (index % 7 is 0)
			{
				viewModel.Refresh();
			}
		}

		viewModel.Refresh();

		using (Assert.Multiple())
		{
			await Assert.That(viewModel.Entries.Count).IsEqualTo(DanmakuConsts.MaxFeedEntries);
			await Assert.That(viewModel.Entries[0].Text).IsEqualTo("5");
			await Assert.That(viewModel.Entries[^1].Text).IsEqualTo((DanmakuConsts.MaxFeedEntries + 4).ToString(System.Globalization.CultureInfo.InvariantCulture));
		}
	}

	[Test]
	public async Task NewEntriesAreCountedWhileNotFollowingUntilScrolledBackAsync()
	{
		FakeDanmakuFeed feed = new(1);
		feed.Add(FakeDanmakuFeed.Comment("a"));
		using DanmakuFilterViewModel filter = new();
		using LiveDanmakuViewModel viewModel = new(feed, filter, false);

		viewModel.IsFollowing = false;
		feed.Add(FakeDanmakuFeed.Comment("b"), FakeDanmakuFeed.Comment("c"));
		viewModel.Refresh();
		(int count, string? text) = (viewModel.NewEntryCount, viewModel.NewEntriesText);
		((ICommand)viewModel.ScrollToLatestCommand).Execute(null);

		using (Assert.Multiple())
		{
			await Assert.That(count).IsEqualTo(2);
			await Assert.That(text).IsEqualTo(AppLanguage.Current.NewDanmakuCount.ToString("2"));
			await Assert.That(viewModel.IsFollowing).IsTrue();
			await Assert.That(viewModel.NewEntryCount).IsEqualTo(0);
			await Assert.That(viewModel.NewEntriesText).IsNull();
		}
	}

	[Test]
	public async Task EntriesStayPutWhileNotFollowingAsync()
	{
		FakeDanmakuFeed feed = new(1);
		feed.Add(FakeDanmakuFeed.Comment("a"), FakeDanmakuFeed.Comment("b"));
		using DanmakuFilterViewModel filter = new();
		using LiveDanmakuViewModel viewModel = new(feed, filter, false);
		DanmakuEntryViewModel first = viewModel.Entries[0];

		viewModel.IsFollowing = false;

		for (int index = 0; index < DanmakuConsts.MaxFeedEntries; ++index)
		{
			feed.Add(FakeDanmakuFeed.Comment(index.ToString(System.Globalization.CultureInfo.InvariantCulture)));

			if (index % 50 is 0)
			{
				viewModel.Refresh();
			}
		}

		viewModel.Refresh();
		(string[] whileReading, DanmakuEntryViewModel head, int newEntries) = (Texts(viewModel), viewModel.Entries[0], viewModel.NewEntryCount);
		viewModel.IsFollowing = true;

		using (Assert.Multiple())
		{
			await Assert.That(whileReading).IsEquivalentTo(["a", "b"], EqualityComparer<string>.Default, CollectionOrdering.Matching);
			await Assert.That(head).IsSameReferenceAs(first);
			await Assert.That(newEntries).IsEqualTo(DanmakuConsts.MaxFeedEntries);
			await Assert.That(viewModel.Entries.Count).IsEqualTo(DanmakuConsts.MaxFeedEntries);
			await Assert.That(viewModel.Entries[0].Text).IsEqualTo("0");
			await Assert.That(viewModel.Entries[^1].Text).IsEqualTo((DanmakuConsts.MaxFeedEntries - 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
		}
	}

	[Test]
	public async Task CategoryFiltersApplyToShownAndNewEntriesAsync()
	{
		FakeDanmakuFeed feed = new(1);
		DanmuSender sender = new(2, "老板", GuardLevel.None, null);
		feed.Add(FakeDanmakuFeed.Comment("a"), new DanmakuEntryDto.SuperChat(1, sender, "sc", 30_000, TimeSpan.FromMinutes(1)));
		using DanmakuFilterViewModel filter = new();
		using LiveDanmakuViewModel viewModel = new(feed, filter, false);

		viewModel.Filter.ShowComments = false;
		string[] filtered = Texts(viewModel);
		feed.Add(FakeDanmakuFeed.Comment("b"), new DanmakuEntryDto.Gift(sender, "小花花", 1, 100));
		viewModel.Refresh();

		using (Assert.Multiple())
		{
			await Assert.That(filtered).IsEquivalentTo(["sc"], EqualityComparer<string>.Default, CollectionOrdering.Matching);
			await Assert.That(viewModel.Entries.Select(static entry => entry.Kind).ToArray()).IsEquivalentTo([DanmakuEntryKind.SuperChat, DanmakuEntryKind.Gift], EqualityComparer<DanmakuEntryKind>.Default, CollectionOrdering.Matching);
			await Assert.That((viewModel.HasEntries, viewModel.HasVisibleEntries)).IsEqualTo((true, true));
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task SearchMatchesNamesAndTextAsync(CancellationToken cancellationToken)
	{
		FakeDanmakuFeed feed = new(1);
		feed.Add(FakeDanmakuFeed.Comment("你好", "甲"), FakeDanmakuFeed.Comment("晚上好", "乙"), FakeDanmakuFeed.Comment("再见", "好人"));
		using DanmakuFilterViewModel filter = new();
		using LiveDanmakuViewModel viewModel = new(feed, filter, false);

		viewModel.Filter.SearchText = "晚上";

		await TestWait.UntilAsync(() => viewModel.Entries.Count is 1, "the search by text is applied", cancellationToken);

		string[] byText = Texts(viewModel);
		viewModel.Filter.SearchText = "好人";

		await TestWait.UntilAsync(() => viewModel.Entries is [{ Text: "再见" }], "the search by name is applied", cancellationToken);

		await Assert.That(byText).IsEquivalentTo(["晚上好"], EqualityComparer<string>.Default, CollectionOrdering.Matching);
	}

	public static IEnumerable<Func<(DanmakuFeedStatus Status, string? Identity, bool CanLogin)>> IdentityCases()
	{
		yield return () => (new DanmakuFeedStatus(DanmakuConnectionState.Connecting, 0, null, false, 0, null), null, false);
		yield return () => (new DanmakuFeedStatus(DanmakuConnectionState.Connected, 1, 0, true, 0, null), AppLanguage.Current.DanmakuAnonymousMasked, true);
		yield return () => (new DanmakuFeedStatus(DanmakuConnectionState.Connected, 1, 0, false, 0, null), AppLanguage.Current.DanmakuAnonymous, false);
		yield return () => (new DanmakuFeedStatus(DanmakuConnectionState.Connected, 1, 42, false, 0, null), AppLanguage.Current.DanmakuLoggedIn, false);
	}

	[Test]
	[MethodDataSource(nameof(IdentityCases))]
	public async Task OnlyAMaskedAnonymousConnectionOffersToLogInAsync(DanmakuFeedStatus status, string? identity, bool canLogin)
	{
		FakeDanmakuFeed feed = new(1) { Status = status };
		using DanmakuFilterViewModel filter = new();
		using LiveDanmakuViewModel viewModel = new(feed, filter, false);

		using (Assert.Multiple())
		{
			await Assert.That(viewModel.IdentityText).IsEqualTo(identity);
			await Assert.That(viewModel.CanLogin).IsEqualTo(canLogin);
		}
	}

	// 重连可能快到两次刷新之间看不到连接中
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task LoggingInWaitsForTheReconnectionAsync(bool seesConnecting)
	{
		FakeDanmakuFeed feed = new(1);
		using DanmakuFilterViewModel filter = new();
		using LiveDanmakuViewModel viewModel = new(feed, filter, false);
		bool before = viewModel.CanLogin;

		viewModel.OnLoggedIn();
		(bool CanLogin, string? Identity) pending = (viewModel.CanLogin, viewModel.IdentityText);
		viewModel.Refresh();
		(bool CanLogin, string? Identity) unchanged = (viewModel.CanLogin, viewModel.IdentityText);
		DanmakuFeedStatus anonymous = feed.Status;

		if (seesConnecting)
		{
			feed.Status = anonymous with { State = DanmakuConnectionState.Connecting };
			viewModel.Refresh();
		}

		(bool CanLogin, string? Identity) reconnecting = (viewModel.CanLogin, viewModel.IdentityText);
		feed.Status = anonymous with { Connection = anonymous.Connection + 1 };
		viewModel.Refresh();

		using (Assert.Multiple())
		{
			await Assert.That(before).IsTrue();
			await Assert.That(pending).IsEqualTo((false, (string?)AppLanguage.Current.DanmakuLoginPending));
			await Assert.That(unchanged).IsEqualTo(pending);
			await Assert.That(reconnecting).IsEqualTo(pending);
			await Assert.That((viewModel.CanLogin, viewModel.IdentityText)).IsEqualTo((true, (string?)AppLanguage.Current.DanmakuAnonymousMasked));
		}
	}

	[Test]
	public async Task StatusIsReadEvenWhenTheVersionIsUnchangedAsync()
	{
		FakeDanmakuFeed feed = new(1);
		using DanmakuFilterViewModel filter = new();
		using LiveDanmakuViewModel viewModel = new(feed, filter, false);
		DateTimeOffset disconnectedAt = new(2026, 11, 22, 20, 0, 0, TimeSpan.FromHours(8));

		feed.Status = new DanmakuFeedStatus(DanmakuConnectionState.Disconnected, 1, 0, true, 12.4, disconnectedAt);
		viewModel.Refresh();

		using (Assert.Multiple())
		{
			await Assert.That(viewModel.State).IsEqualTo(DanmakuConnectionState.Disconnected);
			await Assert.That(viewModel.StateText).IsEqualTo(AppLanguage.Current.DanmakuDisconnected);
			await Assert.That(viewModel.RateText).IsEqualTo(AppLanguage.Current.DanmakuRate.ToString("12"));
			await Assert.That(viewModel.ReconnectingText).IsEqualTo(AppLanguage.Current.DanmakuReconnecting.ToString(disconnectedAt.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)));
		}
	}

	[Test]
	public async Task SuperChatDeletionMarksTheShownSuperChatAsync()
	{
		FakeDanmakuFeed feed = new(1);
		DanmuSender sender = new(2, "老板", GuardLevel.None, null);
		feed.Add(new DanmakuEntryDto.SuperChat(7, sender, "被删", 30_000, TimeSpan.FromMinutes(1)), new DanmakuEntryDto.SuperChat(8, sender, "留着", 30_000, TimeSpan.FromMinutes(1)));
		using DanmakuFilterViewModel filter = new();
		using LiveDanmakuViewModel viewModel = new(feed, filter, false);
		DanmakuEntryViewModel shown = viewModel.Entries[0];
		bool notified = false;
		shown.PropertyChanged += (_, e) => notified |= e.PropertyName is nameof(DanmakuEntryViewModel.IsDeleted);

		feed.Add(new DanmakuEntryDto.SuperChatDeletion([7]), FakeDanmakuFeed.Comment("a"));
		viewModel.Refresh();

		using (Assert.Multiple())
		{
			await Assert.That(Texts(viewModel)).IsEquivalentTo(["被删", "留着", "a"], EqualityComparer<string>.Default, CollectionOrdering.Matching);
			await Assert.That(viewModel.Entries.Select(static entry => entry.IsDeleted).ToArray()).IsEquivalentTo([true, false, false], EqualityComparer<bool>.Default, CollectionOrdering.Matching);
			await Assert.That(notified).IsTrue();
		}
	}

	private static string[] Texts(LiveDanmakuViewModel viewModel)
	{
		return [.. viewModel.Entries.Select(static entry => entry.Text)];
	}
}
