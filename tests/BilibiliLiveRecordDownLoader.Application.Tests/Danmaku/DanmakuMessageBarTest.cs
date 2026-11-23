using BilibiliLiveRecordDownLoader.Application.Danmaku;
using TUnit.Assertions.Enums;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Danmaku;

public sealed class DanmakuMessageBarTest
{
	private const double MaxHeight = 100;
	private const double Gap = 10;

	private static TimeSpan S(double seconds)
	{
		return TimeSpan.FromMilliseconds(Math.Round(seconds * 1000));
	}

	private static DanmakuMessageRequest SuperChat(double time, double height = 30, double duration = 20)
	{
		return new DanmakuMessageRequest(S(time), DanmakuMessageKind.SuperChat, height, S(duration), 1, null);
	}

	private static DanmakuMessageRequest Guard(double time, double height = 30)
	{
		return new DanmakuMessageRequest(S(time), DanmakuMessageKind.Guard, height, S(6), 1, null);
	}

	private static DanmakuMessageRequest Gift(double time, string key = "a", int count = 1, double height = 30)
	{
		return new DanmakuMessageRequest(S(time), DanmakuMessageKind.Gift, height, DanmakuMessageBar.ComboWindow, count, key);
	}

	private static DanmakuMessagePlacement Of(DanmakuMessageLayout layout, int request)
	{
		return layout.Placements.Single(placement => placement.Request == request);
	}

	// 同一帧里的消息共用接收时刻，第一段就要用合并后的数量
	[Test]
	public async Task ComboMergedAtTheMomentItIsShownStartsWithTheMergedCountAsync()
	{
		DanmakuMessageLayout layout = DanmakuMessageBar.Layout([Gift(0), Gift(0, count: 2)], MaxHeight, Gap);

		using (Assert.Multiple())
		{
			await Assert.That(Of(layout, 0).Spans).IsEquivalentTo([new DanmakuMessageSpan(S(0), DanmakuMessageBar.ComboWindow, 0, 0, S(0), 3)]);
			await Assert.That((layout.Shown, layout.Dropped)).IsEqualTo((2, 0));
		}
	}

	[Test]
	public async Task NewEntriesGoAtTheBottomAndPushOlderOnesUpAsync()
	{
		DanmakuMessageLayout layout = DanmakuMessageBar.Layout([SuperChat(0, 20, 10), Guard(1)], MaxHeight, Gap);
		DanmakuMessagePlacement superChat = Of(layout, 0);
		DanmakuMessagePlacement guard = Of(layout, 1);

		using (Assert.Multiple())
		{
			await Assert.That(superChat.Spans).IsEquivalentTo(
			[
				new DanmakuMessageSpan(S(0), S(1), 0, 0, S(0), 1),
				// 上舰从下面进来，SC 在 200 毫秒里升到上舰上方
				new DanmakuMessageSpan(S(1), S(7), 0, 40, S(1.2), 1),
				// 上舰到期后落回原位
				new DanmakuMessageSpan(S(7), S(10), 40, 0, S(7.2), 1)
			], CollectionOrdering.Matching);
			await Assert.That(guard.Spans).IsEquivalentTo([new DanmakuMessageSpan(S(1), S(7), 0, 0, S(1), 1)]);
			await Assert.That((guard.ShownAt, guard.FadeOutStart, guard.RemovedAt)).IsEqualTo((S(1), S(6.8), S(7)));
			await Assert.That((layout.Shown, layout.Dropped)).IsEqualTo((2, 0));
		}
	}

	[Test]
	public async Task AChangeDuringAMoveContinuesFromWhereTheEntryIsAsync()
	{
		DanmakuMessageLayout layout = DanmakuMessageBar.Layout([SuperChat(0, 20), Guard(1), Guard(1.1)], MaxHeight, Gap);

		// 1.1 秒时 SC 刚升到 20，再从这里升到 80
		await Assert.That(Of(layout, 0).Spans[2]).IsEqualTo(new DanmakuMessageSpan(S(1.1), S(7), 20, 80, S(1.3), 1));
	}

	[Test]
	public async Task WhenFullTheOldestGiftGoesFirstThenTheOldestGuardThenTheOldestSuperChatAsync()
	{
		// 0 秒时三条正好占满 100；之后每来一条 SC 都要挤走一条
		DanmakuMessageLayout layout = DanmakuMessageBar.Layout(
		[
			SuperChat(0, 20),
			Guard(0, 20),
			Gift(0, height: 20),
			SuperChat(4.5, 20),
			SuperChat(4.6, 20),
			SuperChat(4.7, 20)
		], MaxHeight, Gap);

		using (Assert.Multiple())
		{
			await Assert.That((Of(layout, 2).FadeOutStart, Of(layout, 2).RemovedAt)).IsEqualTo((S(4.5), S(4.7)));
			await Assert.That((Of(layout, 1).FadeOutStart, Of(layout, 1).RemovedAt)).IsEqualTo((S(4.6), S(4.8)));
			await Assert.That((Of(layout, 0).FadeOutStart, Of(layout, 0).RemovedAt)).IsEqualTo((S(4.7), S(4.9)));
			// 被挤走的条目原地淡出
			await Assert.That(Of(layout, 2).Spans[^1]).IsEqualTo(new DanmakuMessageSpan(S(4.5), S(4.7), 0, 0, S(4.5), 1));
			await Assert.That(Of(layout, 5).ShownAt).IsEqualTo(S(4.7));
		}
	}

	[Test]
	public async Task AGiftNeverPushesOutASuperChatOrAGuardAndIsDroppedInsteadAsync()
	{
		DanmakuMessageLayout layout = DanmakuMessageBar.Layout([SuperChat(0, 40), Guard(0, 40), Gift(5, height: 30)], MaxHeight, Gap);

		using (Assert.Multiple())
		{
			await Assert.That(layout.Placements.Select(static placement => placement.Request)).IsEquivalentTo([0, 1]);
			await Assert.That((layout.Shown, layout.Dropped)).IsEqualTo((2, 1));
		}
	}

	[Test]
	public async Task EntriesStayAtLeastFourSecondsAndSuperChatsWaitForTheirTurnAsync()
	{
		DanmakuMessageLayout layout = DanmakuMessageBar.Layout([Gift(0, height: 60), Gift(1, "b", height: 30), SuperChat(2, 60)], MaxHeight, Gap);

		using (Assert.Multiple())
		{
			// 第一条礼物满 4 秒时被挤走，SC 立刻补上
			await Assert.That(Of(layout, 0).RemovedAt).IsEqualTo(S(4.2));
			await Assert.That(Of(layout, 2).ShownAt).IsEqualTo(S(4));
			await Assert.That(Of(layout, 1).RemovedAt).IsEqualTo(S(6));
			await Assert.That((layout.Shown, layout.Dropped)).IsEqualTo((3, 0));
		}
	}

	[Test]
	public async Task WaitingEntriesKeepTheirOrderAndGiftsAreDroppedMeanwhileAsync()
	{
		DanmakuMessageLayout layout = DanmakuMessageBar.Layout(
		[
			SuperChat(0, 100, 10),
			Guard(1, 100),
			SuperChat(2, 100, 8),
			Gift(3, height: 10)
		], MaxHeight, Gap);

		using (Assert.Multiple())
		{
			// 上舰挤不走 SC，等它到期；后到的 SC 排在上舰后面，等上舰满 4 秒再挤走它
			await Assert.That(Of(layout, 1).ShownAt).IsEqualTo(S(10));
			await Assert.That(Of(layout, 1).RemovedAt).IsEqualTo(S(14.2));
			await Assert.That(Of(layout, 2).ShownAt).IsEqualTo(S(14));
			await Assert.That(Of(layout, 2).RemovedAt).IsEqualTo(S(22));
			await Assert.That((layout.Shown, layout.Dropped)).IsEqualTo((3, 1));
		}
	}

	[Test]
	public async Task AGiftThatWouldFitIsStillDroppedWhileAnEntryIsWaitingAsync()
	{
		DanmakuMessageLayout layout = DanmakuMessageBar.Layout([SuperChat(0, 60, 10), Guard(1, 50), Gift(2, height: 20)], MaxHeight, Gap);

		using (Assert.Multiple())
		{
			await Assert.That(Of(layout, 1).ShownAt).IsEqualTo(S(10));
			await Assert.That(layout.Placements.Select(static placement => placement.Request)).IsEquivalentTo([0, 1]);
			await Assert.That((layout.Shown, layout.Dropped)).IsEqualTo((2, 1));
		}
	}

	[Test]
	public async Task AnEntryTallerThanTheBarStillShowsWhenTheBarIsEmptyAsync()
	{
		DanmakuMessageLayout layout = DanmakuMessageBar.Layout([SuperChat(0, 150, 10)], MaxHeight, Gap);

		await Assert.That(Of(layout, 0).Spans).IsEquivalentTo([new DanmakuMessageSpan(S(0), S(10), 0, 0, S(0), 1)]);
	}

	[Test]
	public async Task GiftsFromTheSameSenderWithinFiveSecondsMergeIntoOneComboAsync()
	{
		DanmakuMessageLayout layout = DanmakuMessageBar.Layout([Gift(0), Gift(2, count: 2), Gift(6.5, count: 3), Gift(7, "b"), Gift(12)], MaxHeight, Gap);
		DanmakuMessagePlacement combo = Of(layout, 0);

		using (Assert.Multiple())
		{
			await Assert.That(combo.Spans.Select(static span => (span.Start, span.Count))).IsEquivalentTo([(S(0), 1), (S(2), 3), (S(6.5), 6), (S(7), 6)], EqualityComparer<(TimeSpan, int)>.Default, CollectionOrdering.Matching);
			// 最后一次到达之后再显示 5 秒
			await Assert.That(combo.RemovedAt).IsEqualTo(S(11.5));
			// 别人的礼物另起一条，上一次连击结束后再送也另起一条
			await Assert.That(layout.Placements.Select(static placement => placement.Request)).IsEquivalentTo([0, 3, 4]);
			await Assert.That((layout.Shown, layout.Dropped)).IsEqualTo((5, 0));
		}
	}

	[Test]
	public async Task AComboThatWasPushedOutStartsOverAsync()
	{
		DanmakuMessageLayout layout = DanmakuMessageBar.Layout([Gift(0, height: 60), Gift(4, "b", height: 60), Gift(4.5, height: 60)], MaxHeight, Gap);

		using (Assert.Multiple())
		{
			await Assert.That(Of(layout, 0).RemovedAt).IsEqualTo(S(4.2));
			// 第一条已经被挤走，同一连击的下一次到达放不下，只能丢弃
			await Assert.That((layout.Shown, layout.Dropped)).IsEqualTo((2, 1));
		}
	}
}
