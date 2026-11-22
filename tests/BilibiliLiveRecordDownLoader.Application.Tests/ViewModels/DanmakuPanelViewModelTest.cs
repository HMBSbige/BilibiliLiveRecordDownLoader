using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using BilibiliLiveRecordDownLoader.ViewModels.Danmaku;
using BilibiliLiveRecordDownLoader.ViewModels.Rooms;
using Splat;
using System.Windows.Input;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class DanmakuPanelViewModelTest
{
	// 列表项读取 TimeProvider 时走全局定位器
	[Before(Class)]
	public static void RegisterServices()
	{
		AppLocator.CurrentMutable.RegisterConstant(TimeProvider.System);
	}

	private static DanmakuPanelViewModel Create(FakeDanmakuAppService danmaku)
	{
		return new DanmakuPanelViewModel(danmaku, new FakeConfigManager(), static _ => Task.FromResult(false)) { AutoRefresh = false };
	}

	private static RoomItemViewModel Room(long roomId)
	{
		return new RoomItemViewModel(new RoomSnapshot(new RoomSettingsDto { RoomId = roomId }, 0, $"主播{roomId}", null, LiveStatus.Live, RecordStatus.Idle, 0, null, null), false);
	}

	// 面板收起时不订阅，房间就不必为它解析弹幕
	[Test]
	public async Task RoomIsSubscribedOnlyWhileThePanelIsOpenAsync()
	{
		FakeDanmakuAppService danmaku = new();
		using DanmakuPanelViewModel panel = Create(danmaku);
		using RoomItemViewModel room = Room(1);

		panel.Follow(room);
		int whileClosed = danmaku.Feeds.Count;
		panel.IsOpen = true;
		LiveDanmakuViewModel? live = panel.Live;
		((ICommand)panel.CloseCommand).Execute(null);

		using (Assert.Multiple())
		{
			await Assert.That(whileClosed).IsEqualTo(0);
			await Assert.That(live?.RoomId).IsEqualTo(1);
			await Assert.That(danmaku.Feeds.Single().IsDisposed).IsTrue();
			await Assert.That(panel.Live).IsNull();
		}
	}

	[Test]
	public async Task SwitchingRoomsUnsubscribesThePreviousOneAsync()
	{
		FakeDanmakuAppService danmaku = new();
		using DanmakuPanelViewModel panel = Create(danmaku);
		using RoomItemViewModel first = Room(1);
		using RoomItemViewModel second = Room(2);

		panel.IsOpen = true;
		panel.Follow(first);
		panel.Follow(first);
		panel.Follow(second);
		FakeDanmakuFeed[] feeds = [.. danmaku.Feeds];

		using (Assert.Multiple())
		{
			await Assert.That(feeds.Select(static feed => feed.RoomId).ToArray()).IsEquivalentTo([1L, 2L], EqualityComparer<long>.Default);
			await Assert.That(feeds.Select(static feed => feed.IsDisposed).ToArray()).IsEquivalentTo([true, false], EqualityComparer<bool>.Default);
			await Assert.That(panel.Live?.RoomId).IsEqualTo(2);
		}
	}

	[Test]
	public async Task RemovingTheFollowedRoomUnsubscribesAsync()
	{
		FakeDanmakuAppService danmaku = new();
		using DanmakuPanelViewModel panel = Create(danmaku);
		using RoomItemViewModel room = Room(1);

		panel.IsOpen = true;
		panel.Follow(room);
		panel.OnRoomRemoved(room);

		using (Assert.Multiple())
		{
			await Assert.That(danmaku.Feeds.Single().IsDisposed).IsTrue();
			await Assert.That((panel.Room, panel.Live)).IsEqualTo((null, null));
		}
	}

	[Test]
	public async Task RoomRemovedJustBeforeSubscribingIsDroppedAsync()
	{
		FakeDanmakuAppService danmaku = new();
		danmaku.MissingRooms.Add(1);
		using DanmakuPanelViewModel panel = Create(danmaku);
		using RoomItemViewModel room = Room(1);

		panel.IsOpen = true;
		panel.Follow(room);

		await Assert.That((panel.Room, panel.Live)).IsEqualTo((null, null));
	}

	// 看文件时不再为直播间解析，关掉文件从那一刻重新订阅
	[Test]
	[Timeout(30_000)]
	public async Task OpeningAFileReplacesTheLiveFeedUntilItIsClosedAsync(CancellationToken cancellationToken)
	{
		FakeDanmakuAppService danmaku = new() { OpenFile = static _ => DanmakuFileViewModelTest.CreateFile(seconds: [1]) };
		using DanmakuPanelViewModel panel = Create(danmaku);
		using RoomItemViewModel room = Room(1);

		panel.Follow(room);
		await panel.LoadFileAsync("a.xml", cancellationToken);
		(bool isOpen, bool hasFile, int feeds) = (panel.IsOpen, panel.File is not null, danmaku.Feeds.Count(static feed => !feed.IsDisposed));
		((ICommand)panel.CloseFileCommand).Execute(null);

		using (Assert.Multiple())
		{
			await Assert.That((isOpen, hasFile, feeds)).IsEqualTo((true, true, 0));
			await Assert.That(panel.File).IsNull();
			await Assert.That(panel.Live?.RoomId).IsEqualTo(1);
		}
	}

	public static IEnumerable<Func<(Exception Failure, string Expected)>> OpenFailures()
	{
		yield return static () => (new FileNotFoundException(null, "a.xml"), AppLanguage.Current.FileNotFound.ToString("a.xml"));
		yield return static () => (new InvalidDataException("不是弹幕文件"), "不是弹幕文件");
		yield return static () => (new IOException("被占用"), "被占用");
	}

	[Test]
	[Timeout(30_000)]
	[MethodDataSource(nameof(OpenFailures))]
	public async Task FilesThatCannotBeOpenedAreReportedInThePanelAsync(Exception failure, string expected, CancellationToken cancellationToken)
	{
		FakeDanmakuAppService danmaku = new() { OpenFile = _ => throw failure };
		using DanmakuPanelViewModel panel = Create(danmaku);

		await panel.LoadFileAsync("a.xml", cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(panel.IsOpen).IsTrue();
			await Assert.That(panel.ErrorMessage).IsEqualTo(expected);
			await Assert.That(panel.File).IsNull();
			await Assert.That(panel.IsLoadingFile).IsFalse();
		}
	}
}
