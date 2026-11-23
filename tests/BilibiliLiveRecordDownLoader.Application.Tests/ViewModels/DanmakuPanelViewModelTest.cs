using BilibiliLiveRecordDownLoader.Application.Contracts.Danmaku;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using BilibiliLiveRecordDownLoader.ViewModels.Danmaku;
using BilibiliLiveRecordDownLoader.ViewModels.Rooms;
using System.Windows.Input;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class DanmakuPanelViewModelTest
{
	private static DanmakuPanelViewModel Create(IDanmakuAppService danmaku, IRoomAppService? rooms = null, ISettingsAppService? settings = null)
	{
		return new DanmakuPanelViewModel(danmaku, rooms ?? new NoRooms(), settings ?? new SettingsAppService(new FakeConfigManager()), static _ => Task.FromResult(false)) { AutoRefresh = false };
	}

	private static RoomItemViewModel Room(long roomId)
	{
		return new RoomItemViewModel(new RoomSnapshot(new RoomSettingsDto { RoomId = roomId }, 0, $"主播{roomId}", null, LiveStatus.Live, RecordStatus.Idle, 0, null, null), false);
	}

	private static Func<string, CancellationToken, Task<DanmakuFileDto>> OpenFileAfter(Task gate)
	{
		return async (_, cancellationToken) =>
		{
			await gate.WaitAsync(cancellationToken);
			return DanmakuFileViewModelTest.CreateFile(seconds: [1]);
		};
	}

	[Test]
	public async Task RoomIsSubscribedOnlyWhileThePanelIsOpenAsync()
	{
		FakeDanmakuAppService danmaku = new();
		using DanmakuPanelViewModel panel = Create(danmaku);
		using RoomItemViewModel room = Room(1);

		panel.Follow(room);
		FakeDanmakuFeed[] whileClosed = [.. danmaku.Feeds];
		panel.IsOpen = true;
		long? whileOpen = panel.Live?.RoomId;
		((ICommand)panel.CloseCommand).Execute(null);
		FakeDanmakuFeed feed = await Assert.That(danmaku.Feeds).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(whileClosed).IsEmpty();
			await Assert.That(whileOpen).IsEqualTo(1);
			await Assert.That((feed.IsDisposed, panel.Live)).IsEqualTo((true, null));
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

		using (Assert.Multiple())
		{
			await Assert.That(danmaku.Feeds.Select(static feed => (feed.RoomId, feed.IsDisposed))).IsEquivalentTo([(1L, true), (2L, false)], CollectionOrdering.Matching).Because("同一房间跟随两次也只订阅一次");
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
		FakeDanmakuFeed feed = await Assert.That(danmaku.Feeds).HasSingleItem();

		await Assert.That((feed.IsDisposed, panel.Room, panel.Live)).IsEqualTo((true, null, null));
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
	public async Task OpeningAFileReplacesTheLiveFeedUntilItIsClosedAsync(CancellationToken cancellationToken)
	{
		FakeDanmakuAppService danmaku = new() { OpenFile = static (_, _) => Task.FromResult(DanmakuFileViewModelTest.CreateFile(seconds: [1])) };
		using DanmakuPanelViewModel panel = Create(danmaku);
		using RoomItemViewModel room = Room(1);

		panel.Follow(room);
		await panel.LoadFileAsync("a.xml", cancellationToken);
		(bool IsOpen, bool HasFile, int Feeds) withFile = (panel.IsOpen, panel.File is not null, danmaku.Feeds.Count(static feed => !feed.IsDisposed));
		((ICommand)panel.CloseFileCommand).Execute(null);

		using (Assert.Multiple())
		{
			await Assert.That(withFile).IsEqualTo((true, true, 0));
			await Assert.That((panel.File, panel.Live?.RoomId)).IsEqualTo((null, 1));
		}
	}

	[Test]
	public async Task ShowingLiveDanmakuClosesTheOpenFileAsync(CancellationToken cancellationToken)
	{
		FakeDanmakuAppService danmaku = new() { OpenFile = static (_, _) => Task.FromResult(DanmakuFileViewModelTest.CreateFile(seconds: [1])) };
		using DanmakuPanelViewModel panel = Create(danmaku);
		using RoomItemViewModel first = Room(1);
		using RoomItemViewModel second = Room(2);

		panel.Follow(first);
		await panel.LoadFileAsync("a.xml", cancellationToken);
		panel.ShowLive(second);

		using (Assert.Multiple())
		{
			await Assert.That((panel.IsOpen, panel.File, panel.Room)).IsEqualTo((true, null, second));
			await Assert.That(danmaku.Feeds.Select(static feed => (feed.RoomId, feed.IsDisposed))).IsEquivalentTo([(2L, false)], CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task LoadingAFileShowsThePanelRightAwayAsync(CancellationToken cancellationToken)
	{
		TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
		FakeDanmakuAppService danmaku = new() { OpenFile = OpenFileAfter(gate.Task) };
		using DanmakuPanelViewModel panel = Create(danmaku);
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
	public async Task ClosingThePanelWhileLoadingDiscardsTheFileAsync(CancellationToken cancellationToken)
	{
		TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
		using DanmakuPanelViewModel panel = Create(new FakeDanmakuAppService { OpenFile = OpenFileAfter(gate.Task) });

		Task loading = panel.LoadFileAsync("a.xml", cancellationToken);
		((ICommand)panel.CloseCommand).Execute(null);
		gate.SetResult();
		await loading;

		await Assert.That((panel.IsOpen, panel.File, panel.IsLoadingFile)).IsEqualTo((false, null, false));
	}

	[Test]
	public async Task FilePickerStartsInTheExpectedDirectoryAsync(CancellationToken cancellationToken)
	{
		NoRooms rooms = new();
		SettingsAppService settings = new(new FakeConfigManager());
		FakeDanmakuAppService danmaku = new() { OpenFile = static (_, _) => Task.FromResult(DanmakuFileViewModelTest.CreateFile(seconds: [1])) };
		using DanmakuPanelViewModel panel = Create(danmaku, rooms, settings);
		using RoomItemViewModel first = Room(1);
		using RoomItemViewModel second = Room(2);
		List<string> directories = [];
		panel.PickDanmakuFile = directory =>
		{
			directories.Add(directory);
			return Task.FromResult<string?>(null);
		};

		await panel.PickFileAsync(null, cancellationToken);
		panel.Follow(first);
		await panel.PickFileAsync(null, cancellationToken);
		await panel.LoadFileAsync("a.xml", cancellationToken);
		await panel.PickFileAsync(null, cancellationToken);
		await panel.PickFileAsync(second, cancellationToken);

		string recordDirectory = (await settings.GetAsync(cancellationToken)).RecordDirectory;
		string fileDirectory = Path.GetDirectoryName(DanmakuFileViewModelTest.CreateFile(seconds: [1]).Path) ?? string.Empty;

		await Assert.That(directories).IsEquivalentTo([recordDirectory, await rooms.GetDirectoryAsync(1, cancellationToken), fileDirectory, await rooms.GetDirectoryAsync(2, cancellationToken)], CollectionOrdering.Matching);
	}

	// 失败的类别由 DanmakuFileViewModelTest 逐一覆盖，面板和它共用同一套判断
	public static IEnumerable<Func<(Exception Failure, string Message)>> OpenFailures()
	{
		yield return static () => (new FileNotFoundException(null, "a.xml"), AppLanguage.Current.FileNotFound.ToString("a.xml"));
		yield return static () => (new InvalidDataException("不是弹幕文件"), "不是弹幕文件");
	}

	[Test]
	[MethodDataSource(nameof(OpenFailures))]
	public async Task FilesThatCannotBeOpenedAreReportedInThePanelAsync(Exception failure, string message, CancellationToken cancellationToken)
	{
		FakeDanmakuAppService danmaku = new() { OpenFile = (_, _) => throw failure };
		using DanmakuPanelViewModel panel = Create(danmaku);

		await panel.LoadFileAsync("a.xml", cancellationToken);

		await Assert.That((panel.IsOpen, panel.ErrorMessage, panel.File, panel.IsLoadingFile)).IsEqualTo((true, message, null, false));
	}

	private sealed class NoRooms : RoomAppServiceStub;
}
