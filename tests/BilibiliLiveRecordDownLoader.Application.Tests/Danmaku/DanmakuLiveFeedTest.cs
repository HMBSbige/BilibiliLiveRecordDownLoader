using BilibiliLive.Danmu;
using BilibiliLiveRecordDownLoader.Application.Contracts.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Danmaku;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Danmaku;

public sealed class DanmakuLiveFeedTest
{
	private readonly FakeTimeProvider _time = new();

	private DanmuMessage.Comment Comment(int index)
	{
		return new DanmuMessage.Comment(new DanmuSender(index, "观众", GuardLevel.None, null), "文本", 1, 25, 0xFFFFFF, DateTimeOffset.UnixEpoch, "0", false) { ReceivedAt = new DanmuReceiveTime(_time.GetTimestamp(), _time.GetUtcNow()) };
	}

	[Test]
	public async Task EntriesAfterASequenceAreReturnedInOrderAsync()
	{
		DanmakuLiveFeed feed = new(_time);

		for (int i = 1; i <= 5; ++i)
		{
			feed.Add(Comment(i));
		}

		feed.Add(new DanmuMessage.LiveStarted());

		using (Assert.Multiple())
		{
			await Assert.That(feed.GetEntries(3).Select(static entry => entry.Sequence)).IsEquivalentTo([4L, 5], CollectionOrdering.Matching);
			await Assert.That(feed.GetEntries(5)).IsEmpty();
			await Assert.That(feed.GetEntries(0)).Count().IsEqualTo(5);
		}
	}

	[Test]
	public async Task OnlyTheLatestEntriesAreKeptAsync()
	{
		DanmakuLiveFeed feed = new(_time);

		for (int i = 1; i <= DanmakuConsts.MaxFeedEntries + 10; ++i)
		{
			feed.Add(Comment(i));
		}

		IReadOnlyList<LiveDanmakuEntryDto> entries = feed.GetEntries(0);

		using (Assert.Multiple())
		{
			await Assert.That(entries).Count().IsEqualTo(DanmakuConsts.MaxFeedEntries);
			await Assert.That((entries[0].Sequence, entries[^1].Sequence)).IsEqualTo((11L, DanmakuConsts.MaxFeedEntries + 10L));
			await Assert.That(feed.GetEntries(DanmakuConsts.MaxFeedEntries + 5)).Count().IsEqualTo(5);
		}
	}

	[Test]
	public async Task RateCountsTheLastMinuteAsync()
	{
		DanmakuLiveFeed feed = new(_time);
		feed.Add(Comment(1));
		_time.Advance(TimeSpan.FromSeconds(30));
		feed.Add(Comment(2));
		feed.Add(Comment(3));
		int both = feed.Status.MessagesPerMinute;
		_time.Advance(TimeSpan.FromSeconds(31));
		int later = feed.Status.MessagesPerMinute;
		_time.Advance(TimeSpan.FromMinutes(5));

		await Assert.That((both, later, feed.Status.MessagesPerMinute)).IsEqualTo((3, 2, 0));
	}

	[Test]
	public async Task ConnectionStateIsReportedWithTheLastDisconnectAsync()
	{
		DanmakuLiveFeed feed = new(_time);
		DanmakuFeedStatus initial = feed.Status;
		feed.SetConnected(0);
		feed.SetMasked();
		DanmakuFeedStatus masked = feed.Status;
		DateTimeOffset lost = _time.GetUtcNow();
		feed.SetDisconnected(lost);
		_time.Advance(TimeSpan.FromSeconds(2));
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
		DanmakuLiveFeed feed = new(_time);
		feed.Add(Comment(1));
		feed.Clear();
		feed.Add(Comment(2));

		await Assert.That(feed.GetEntries(0).Select(static entry => entry.Sequence)).IsEquivalentTo([2L]);
	}
}
