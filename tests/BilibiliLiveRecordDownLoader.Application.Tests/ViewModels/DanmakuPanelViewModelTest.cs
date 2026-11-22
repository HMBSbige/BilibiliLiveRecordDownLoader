using BilibiliLiveRecordDownLoader.Application.Contracts.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using BilibiliLiveRecordDownLoader.ViewModels.Danmaku;
using BilibiliLiveRecordDownLoader.ViewModels.Rooms;
using Splat;
using System.Windows.Input;
using TUnit.Assertions.Enums;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class DanmakuPanelViewModelTest
{
	// 列表项读取 TimeProvider 时走全局定位器
	[Before(Class)]
	public static void RegisterServices()
	{
		AppLocator.CurrentMutable.RegisterConstant(TimeProvider.System);
	}

	private static DanmakuPanelViewModel Create(IDanmakuAppService danmaku, IAppConfigManager? configManager = null, Func<CancellationToken, Task<bool>>? showLogin = null)
	{
		return new DanmakuPanelViewModel(danmaku, configManager ?? new FakeConfigManager(), showLogin ?? (static _ => Task.FromResult(false))) { AutoRefresh = false };
	}

	private static RoomItemViewModel Room(long roomId)
	{
		return new RoomItemViewModel(new RoomSnapshot(new RoomSettingsDto { RoomId = roomId }, 0, $"主播{roomId}", null, LiveStatus.Live, RecordStatus.Idle, 0, null, null), false);
	}

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
		(long RoomId, bool IsDisposed)[] feeds = [.. danmaku.Feeds.Select(static feed => (feed.RoomId, feed.IsDisposed))];

		using (Assert.Multiple())
		{
			// 同一房间跟随两次也只订阅一次
			await Assert.That(feeds).IsEquivalentTo([(1L, true), (2L, false)], EqualityComparer<(long, bool)>.Default, CollectionOrdering.Matching);
			await Assert.That(panel.Live?.RoomId).IsEqualTo(2);
		}
	}

	[Test]
	public async Task FiltersAreKeptWhenSwitchingRoomsAsync()
	{
		FakeDanmakuAppService danmaku = new();
		using DanmakuPanelViewModel panel = Create(danmaku);
		using RoomItemViewModel first = Room(1);
		using RoomItemViewModel second = Room(2);

		panel.IsOpen = true;
		panel.Follow(first);
		DanmakuFilterViewModel? filter = panel.Live?.Filter;
		filter?.ShowGifts = false;
		panel.Follow(second);

		using (Assert.Multiple())
		{
			await Assert.That(panel.Live?.RoomId).IsEqualTo(2);
			await Assert.That(panel.Live?.Filter).IsSameReferenceAs(filter);
			await Assert.That(panel.Live?.Filter.ShowGifts).IsFalse();
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

	[Test]
	[Timeout(30_000)]
	public async Task ShowingLiveDanmakuClosesTheOpenFileAsync(CancellationToken cancellationToken)
	{
		FakeDanmakuAppService danmaku = new() { OpenFile = static _ => DanmakuFileViewModelTest.CreateFile(seconds: [1]) };
		using DanmakuPanelViewModel panel = Create(danmaku);
		using RoomItemViewModel first = Room(1);
		using RoomItemViewModel second = Room(2);

		panel.Follow(first);
		await panel.LoadFileAsync("a.xml", cancellationToken);
		panel.ShowLive(second);

		using (Assert.Multiple())
		{
			await Assert.That((panel.IsOpen, panel.File, panel.Room)).IsEqualTo((true, null, second));
			await Assert.That(danmaku.Feeds.Select(static feed => (feed.RoomId, feed.IsDisposed)).ToArray()).IsEquivalentTo([(2L, false)], EqualityComparer<(long, bool)>.Default, CollectionOrdering.Matching);
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task LoadingAFileShowsThePanelRightAwayAsync(CancellationToken cancellationToken)
	{
		TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
		FakeDanmakuAppService danmaku = new() { OpenFile = static _ => DanmakuFileViewModelTest.CreateFile(seconds: [1]) };
		using DanmakuPanelViewModel panel = Create(new GatedDanmakuAppService(danmaku, gate.Task));
		using RoomItemViewModel room = Room(1);

		panel.Follow(room);
		Task loading = panel.LoadFileAsync("a.xml", cancellationToken);
		(bool IsOpen, bool IsLoading, int Feeds) whileLoading = (panel.IsOpen, panel.IsLoadingFile, danmaku.Feeds.Count);
		gate.SetResult();
		await loading;

		using (Assert.Multiple())
		{
			await Assert.That(whileLoading).IsEqualTo((true, true, 0));
			await Assert.That((panel.IsLoadingFile, panel.File is not null)).IsEqualTo((false, true));
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task ClosingThePanelWhileLoadingDiscardsTheFileAsync(CancellationToken cancellationToken)
	{
		TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
		FakeDanmakuAppService danmaku = new() { OpenFile = static _ => DanmakuFileViewModelTest.CreateFile(seconds: [1]) };
		using DanmakuPanelViewModel panel = Create(new GatedDanmakuAppService(danmaku, gate.Task));

		Task loading = panel.LoadFileAsync("a.xml", cancellationToken);
		((ICommand)panel.CloseCommand).Execute(null);
		gate.SetResult();
		await loading;

		await Assert.That((panel.IsOpen, panel.File, panel.IsLoadingFile)).IsEqualTo((false, null, false));
	}

	[Test]
	[Timeout(30_000)]
	public async Task FilePickerStartsInTheExpectedDirectoryAsync(CancellationToken cancellationToken)
	{
		FakeConfigManager configManager = new();
		string recordDirectory = (await configManager.GetAsync(cancellationToken)).RecordDirectory;
		FakeDanmakuAppService danmaku = new() { OpenFile = static _ => DanmakuFileViewModelTest.CreateFile(seconds: [1]) };
		using DanmakuPanelViewModel panel = Create(danmaku, configManager);
		using RoomItemViewModel first = Room(1);
		using RoomItemViewModel second = Room(2);
		List<string> directories = [];
		panel.PickDanmakuFile = directory =>
		{
			directories.Add(directory);
			return Task.FromResult<string?>(null);
		};

		panel.Follow(first);
		await panel.PickFileAsync(null, cancellationToken);
		await panel.LoadFileAsync("a.xml", cancellationToken);
		await panel.PickFileAsync(null, cancellationToken);
		await panel.PickFileAsync(second, cancellationToken);

		string fileDirectory = Path.GetDirectoryName(DanmakuFileViewModelTest.CreateFile(seconds: [1]).Path) ?? string.Empty;
		string[] expected = [RecordingFileLayout.GetRoomDirectory(recordDirectory, 1), fileDirectory, RecordingFileLayout.GetRoomDirectory(recordDirectory, 2)];

		await Assert.That(directories).IsEquivalentTo(expected, EqualityComparer<string>.Default, CollectionOrdering.Matching);
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

	private sealed class GatedDanmakuAppService(FakeDanmakuAppService inner, Task gate) : IDanmakuAppService
	{
		public IDanmakuFeed Subscribe(long roomId)
		{
			return inner.Subscribe(roomId);
		}

		public async Task<DanmakuFileDto> OpenFileAsync(string path, CancellationToken cancellationToken = default)
		{
			await gate.WaitAsync(cancellationToken);
			return await inner.OpenFileAsync(path, cancellationToken);
		}

		public Task<DanmakuOffsetResult> ChangeOffsetAsync(string path, TimeSpan offset, CancellationToken cancellationToken = default)
		{
			return inner.ChangeOffsetAsync(path, offset, cancellationToken);
		}

		public Task<string> QueueHighlightExportAsync(string danmakuPath, DanmakuHighlightDto highlight, CancellationToken cancellationToken = default)
		{
			return inner.QueueHighlightExportAsync(danmakuPath, highlight, cancellationToken);
		}
	}
}
