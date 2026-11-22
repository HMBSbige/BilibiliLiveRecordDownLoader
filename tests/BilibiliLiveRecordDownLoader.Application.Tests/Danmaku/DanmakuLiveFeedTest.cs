using BilibiliLive.Danmu;
using BilibiliLiveRecordDownLoader.Application.Contracts.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Danmaku;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Danmaku;

public sealed class DanmakuLiveFeedTest
{
	private static DanmuMessage Comment(FakeTimeProvider time, int index)
	{
		return new DanmuMessage.Comment(new DanmuSender(index, "观众", GuardLevel.None, null), "文本", 1, 25, 0xFFFFFF, DateTimeOffset.UnixEpoch, "0", DanmuCommentKind.Text, false) { ReceivedAt = new DanmuReceiveTime(time.GetTimestamp(), time.GetUtcNow()) };
	}

	[Test]
	public async Task EntriesAfterASequenceAreReturnedInOrderAsync()
	{
		FakeTimeProvider time = new();
		DanmakuLiveFeed feed = new(time);

		for (int i = 1; i <= 5; ++i)
		{
			feed.Add(Comment(time, i));
		}

		feed.Add(new DanmuMessage.LiveStarted());

		using (Assert.Multiple())
		{
			await Assert.That(string.Join(", ", feed.GetEntries(3).Select(static entry => entry.Sequence))).IsEqualTo("4, 5");
			await Assert.That(feed.GetEntries(5)).IsEmpty();
			await Assert.That(feed.GetEntries(0).Count).IsEqualTo(5);
		}
	}

	[Test]
	public async Task OnlyTheLatestEntriesAreKeptAsync()
	{
		FakeTimeProvider time = new();
		DanmakuLiveFeed feed = new(time);

		for (int i = 1; i <= DanmakuConsts.MaxFeedEntries + 10; ++i)
		{
			feed.Add(Comment(time, i));
		}

		IReadOnlyList<LiveDanmakuEntryDto> entries = feed.GetEntries(0);

		using (Assert.Multiple())
		{
			await Assert.That(entries.Count).IsEqualTo(DanmakuConsts.MaxFeedEntries);
			await Assert.That(entries[0].Sequence).IsEqualTo(11);
			await Assert.That(entries[^1].Sequence).IsEqualTo(DanmakuConsts.MaxFeedEntries + 10);
			await Assert.That(feed.GetEntries(DanmakuConsts.MaxFeedEntries + 5).Count).IsEqualTo(5);
		}
	}

	[Test]
	public async Task RateCountsTheLastMinuteAsync()
	{
		FakeTimeProvider time = new();
		DanmakuLiveFeed feed = new(time);
		feed.Add(Comment(time, 1));
		time.Advance(TimeSpan.FromSeconds(30));
		feed.Add(Comment(time, 2));
		feed.Add(Comment(time, 3));
		double both = feed.Status.MessagesPerMinute;
		time.Advance(TimeSpan.FromSeconds(31));
		double later = feed.Status.MessagesPerMinute;
		time.Advance(TimeSpan.FromMinutes(5));

		using (Assert.Multiple())
		{
			await Assert.That(both).IsEqualTo(3);
			await Assert.That(later).IsEqualTo(2);
			await Assert.That(feed.Status.MessagesPerMinute).IsEqualTo(0);
		}
	}

	[Test]
	public async Task ConnectionStateIsReportedWithTheLastDisconnectAsync()
	{
		FakeTimeProvider time = new();
		DanmakuLiveFeed feed = new(time);
		DanmakuFeedStatus initial = feed.Status;
		feed.SetConnected(0);
		feed.SetMasked();
		DanmakuFeedStatus masked = feed.Status;
		DateTimeOffset lost = time.GetUtcNow();
		feed.SetDisconnected(lost);
		time.Advance(TimeSpan.FromSeconds(2));
		feed.SetConnecting();
		DanmakuFeedStatus reconnecting = feed.Status;
		long version = feed.Version;
		feed.SetConnected(42);

		using (Assert.Multiple())
		{
			await Assert.That(initial).IsEqualTo(new DanmakuFeedStatus(DanmakuConnectionState.Connecting, 0, null, false, 0, null));
			await Assert.That(masked).IsEqualTo(new DanmakuFeedStatus(DanmakuConnectionState.Connected, 1, 0, true, 0, null));
			await Assert.That(reconnecting).IsEqualTo(new DanmakuFeedStatus(DanmakuConnectionState.Connecting, 1, 0, true, 0, lost));
			await Assert.That(feed.Status).IsEqualTo(new DanmakuFeedStatus(DanmakuConnectionState.Connected, 2, 42, false, 0, null));
			await Assert.That(feed.Version).IsGreaterThan(version);
		}
	}

	[Test]
	public async Task ClearingKeepsTheSequenceGoingAsync()
	{
		FakeTimeProvider time = new();
		DanmakuLiveFeed feed = new(time);
		feed.Add(Comment(time, 1));
		feed.Clear();
		feed.Add(Comment(time, 2));

		await Assert.That(string.Join(", ", feed.GetEntries(0).Select(static entry => entry.Sequence))).IsEqualTo("2");
	}
}
