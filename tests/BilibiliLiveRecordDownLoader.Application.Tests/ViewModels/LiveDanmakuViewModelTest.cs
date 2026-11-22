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
		using LiveDanmakuViewModel viewModel = new(feed, false);
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
		using LiveDanmakuViewModel viewModel = new(feed, false);

		for (int index = 0; index < DanmakuConsts.MaxFeedEntries + 5; ++index)
		{
			feed.Add(FakeDanmakuFeed.Comment(index.ToString(System.Globalization.CultureInfo.InvariantCulture)));

			// 每隔几条刷新一次，像界面那样成批加入
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
		using LiveDanmakuViewModel viewModel = new(feed, false);

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
	public async Task CategoryFiltersApplyToShownAndNewEntriesAsync()
	{
		FakeDanmakuFeed feed = new(1);
		DanmuSender sender = new(2, "老板", GuardLevel.None, null);
		feed.Add(FakeDanmakuFeed.Comment("a"), new DanmakuEntryDto.SuperChat(1, sender, "sc", 30_000, TimeSpan.FromMinutes(1)));
		using LiveDanmakuViewModel viewModel = new(feed, false);

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
		using LiveDanmakuViewModel viewModel = new(feed, false);

		viewModel.Filter.SearchText = "晚上";

		while (viewModel.Entries.Count is not 1)
		{
			await Task.Delay(10, cancellationToken);
		}

		string[] byText = Texts(viewModel);
		viewModel.Filter.SearchText = "好人";

		while (viewModel.Entries is not [{ Text: "再见" }])
		{
			await Task.Delay(10, cancellationToken);
		}

		await Assert.That(byText).IsEquivalentTo(["晚上好"], EqualityComparer<string>.Default, CollectionOrdering.Matching);
	}

	public static IEnumerable<Func<(DanmakuFeedStatus Status, string? Identity, bool CanLogin)>> IdentityCases()
	{
		yield return () => (new DanmakuFeedStatus(DanmakuConnectionState.Connecting, null, false, 0, null, false), null, false);
		yield return () => (new DanmakuFeedStatus(DanmakuConnectionState.Connected, 0, true, 0, null, false), AppLanguage.Current.DanmakuAnonymousMasked, true);
		yield return () => (new DanmakuFeedStatus(DanmakuConnectionState.Connected, 0, false, 0, null, false), AppLanguage.Current.DanmakuAnonymous, false);
		yield return () => (new DanmakuFeedStatus(DanmakuConnectionState.Connected, 42, false, 0, null, true), AppLanguage.Current.DanmakuLoggedIn, false);
	}

	[Test]
	[MethodDataSource(nameof(IdentityCases))]
	public async Task OnlyAMaskedAnonymousConnectionOffersToLogInAsync(DanmakuFeedStatus status, string? identity, bool canLogin)
	{
		FakeDanmakuFeed feed = new(1) { Status = status };
		using LiveDanmakuViewModel viewModel = new(feed, false);

		using (Assert.Multiple())
		{
			await Assert.That(viewModel.IdentityText).IsEqualTo(identity);
			await Assert.That(viewModel.CanLogin).IsEqualTo(canLogin);
		}
	}

	// 速率随时间变化但不改版本号，每次刷新都要读状态
	[Test]
	public async Task StatusIsReadEvenWhenTheVersionIsUnchangedAsync()
	{
		FakeDanmakuFeed feed = new(1);
		using LiveDanmakuViewModel viewModel = new(feed, false);
		DateTimeOffset disconnectedAt = new(2026, 11, 22, 20, 0, 0, TimeSpan.FromHours(8));

		feed.Status = new DanmakuFeedStatus(DanmakuConnectionState.Disconnected, 0, true, 12.4, disconnectedAt, false);
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
	public async Task DisposingUnsubscribesAsync()
	{
		FakeDanmakuFeed feed = new(1);
		LiveDanmakuViewModel viewModel = new(feed, false);

		viewModel.Dispose();

		await Assert.That(feed.IsDisposed).IsTrue();
	}

	// 删除常在 SC 显示之后才到，标在已显示的那一条上，不另占一行
	[Test]
	public async Task SuperChatDeletionMarksTheShownSuperChatAsync()
	{
		FakeDanmakuFeed feed = new(1);
		DanmuSender sender = new(2, "老板", GuardLevel.None, null);
		feed.Add(new DanmakuEntryDto.SuperChat(7, sender, "被删", 30_000, TimeSpan.FromMinutes(1)), new DanmakuEntryDto.SuperChat(8, sender, "留着", 30_000, TimeSpan.FromMinutes(1)));
		using LiveDanmakuViewModel viewModel = new(feed, false);
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
