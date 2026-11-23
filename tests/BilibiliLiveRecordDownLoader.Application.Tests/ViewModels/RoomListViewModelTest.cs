using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using BilibiliLiveRecordDownLoader.ViewModels.Rooms;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;
using System.Collections.Concurrent;
using System.Windows.Input;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class RoomListViewModelTest
{
	private static readonly long[] InitialOrder = [1, 2, 3, 4, 5];

	[Test]
	[Arguments(RoomSortOrder.Custom, RoomFilter.All, true)]
	[Arguments(RoomSortOrder.Custom, RoomFilter.Live, false)]
	[Arguments(RoomSortOrder.Status, RoomFilter.All, false)]
	public async Task MovingIsOnlyAvailableForAllRoomsInTheCustomOrderAsync(RoomSortOrder sortOrder, RoomFilter filter, bool expected)
	{
		using RoomListViewModel viewModel = Create(new FakeRoomAppService(InitialOrder));
		bool[] withoutSelection = CanExecuteMoves(viewModel);

		viewModel.SelectedRooms.Add(viewModel.Rooms[0]);
		((ICommand)viewModel.SortByCommand).Execute(sortOrder);
		((ICommand)viewModel.FilterByCommand).Execute(filter);

		using (Assert.Multiple())
		{
			await Assert.That(viewModel.CanReorder).IsEqualTo(expected);
			await Assert.That(withoutSelection).DoesNotContain(true);
			await Assert.That(CanExecuteMoves(viewModel)).All(canExecute => canExecute == expected);
		}
	}

	[Test]
	[MethodDataSource(nameof(CommandCases))]
	public async Task MoveCommandsMoveTheSelectionPastTheExpectedRoomAsync(string command, long[] selected, long? before, long[] expected, CancellationToken cancellationToken)
	{
		FakeRoomAppService rooms = new(InitialOrder);
		using RoomListViewModel viewModel = Create(rooms);
		Select(viewModel, selected);

		ICommand target = command switch
		{
			"Top" => viewModel.MoveToTopCommand,
			"Bottom" => viewModel.MoveToBottomCommand,
			"Up" => viewModel.MoveUpCommand,
			_ => viewModel.MoveDownCommand
		};

		IReadOnlyList<RoomItemViewModel> moved = await ExecuteAsync(viewModel, () => target.Execute(null), cancellationToken);

		await AssertMovedAsync(viewModel, rooms, moved, selected, before, expected);
	}

	public static IEnumerable<Func<(string Command, long[] Selected, long? Before, long[] Expected)>> CommandCases()
	{
		yield return static () => ("Up", [3], 2, [1, 3, 2, 4, 5]);
		// 不连续的选中项合并到第一个选中项的上一位
		yield return static () => ("Up", [4, 2], 1, [2, 4, 1, 3, 5]);
		yield return static () => ("Up", [1, 3], 2, [1, 3, 2, 4, 5]);
		yield return static () => ("Down", [3], 5, [1, 2, 4, 3, 5]);
		yield return static () => ("Down", [2, 4], null, [1, 3, 5, 2, 4]);
		yield return static () => ("Down", [5], null, [1, 2, 3, 4, 5]);
		yield return static () => ("Top", [5, 3], 1, [3, 5, 1, 2, 4]);
		yield return static () => ("Top", [1, 2], 3, [1, 2, 3, 4, 5]);
		yield return static () => ("Bottom", [1, 3], null, [2, 4, 5, 1, 3]);
	}

	[Test]
	[MethodDataSource(nameof(DropCases))]
	public async Task DroppingMovesTheSelectionBeforeTheRoomAtTheInsertionPointAsync(long[] selected, int index, long? before, long[] expected, CancellationToken cancellationToken)
	{
		FakeRoomAppService rooms = new(InitialOrder);
		using RoomListViewModel viewModel = Create(rooms);
		Select(viewModel, selected);

		IReadOnlyList<RoomItemViewModel> moved = await ExecuteAsync(viewModel, () => ((ICommand)viewModel.MoveSelectionToCommand).Execute(index), cancellationToken);

		await AssertMovedAsync(viewModel, rooms, moved, selected, before, expected);
	}

	public static IEnumerable<Func<(long[] Selected, int Index, long? Before, long[] Expected)>> DropCases()
	{
		yield return static () => ([1], 3, 4, [2, 3, 1, 4, 5]);
		yield return static () => ([2, 3], 0, 1, [2, 3, 1, 4, 5]);
		yield return static () => ([2, 3], 5, null, [1, 4, 5, 2, 3]);
		// 放在被拖动的房间之间时，以后面第一个不移动的房间为准
		yield return static () => ([2, 3], 2, 4, [1, 2, 3, 4, 5]);
		yield return static () => ([4, 1], 3, 5, [2, 3, 1, 4, 5]);
	}

	[Test]
	public async Task SortOrderIsRestoredFromAndSavedToTheConfigAsync(CancellationToken cancellationToken)
	{
		AppConfig config = AppConfig.CreateDefault();
		config.RoomSortOrder = RoomSortOrder.Name;
		FakeConfigManager configManager = new(config);
		using RoomListViewModel viewModel = new(new FakeRoomAppService(InitialOrder), configManager, new FakeDanmakuAppService());

		viewModel.ApplyConfig(config, Signal.Return<string?>(null));
		(RoomSortOrder restored, bool canReorder, int updates) = (viewModel.SortOrder, viewModel.CanReorder, configManager.UpdateCount);
		((ICommand)viewModel.SortByCommand).Execute(RoomSortOrder.Custom);

		await TestWait.UntilAsync(() => config.RoomSortOrder is RoomSortOrder.Custom, "the sort order is saved", cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That((restored, canReorder, updates)).IsEqualTo((RoomSortOrder.Name, false, 0));
			await Assert.That((configManager.UpdateCount, viewModel.CanReorder)).IsEqualTo((1, true));
		}
	}

	[Test]
	public async Task OnlyASingleSelectionIsFollowedByThePanelAsync()
	{
		using RoomListViewModel viewModel = Create(new FakeRoomAppService(InitialOrder));

		Select(viewModel, [2]);
		long? single = viewModel.Danmaku.Room?.RoomId;
		Select(viewModel, [3]);
		long? multiple = viewModel.Danmaku.Room?.RoomId;
		viewModel.SelectedRooms.Clear();

		await Assert.That((single, multiple, viewModel.Danmaku.Room?.RoomId)).IsEqualTo((2, 2, 2));
	}

	[Test]
	public async Task RemovingTheFollowedRoomUnsubscribesThePanelAsync()
	{
		FakeRoomAppService rooms = new(InitialOrder);
		FakeDanmakuAppService danmaku = new();
		using RoomListViewModel viewModel = Create(rooms, danmaku);

		viewModel.Danmaku.IsOpen = true;
		Select(viewModel, [2]);
		rooms.Remove(2);
		viewModel.Sync();

		using (Assert.Multiple())
		{
			await Assert.That(danmaku.Feeds.Select(static feed => (feed.RoomId, feed.IsDisposed))).IsEquivalentTo([(2L, true)]);
			await Assert.That((viewModel.Danmaku.Room, viewModel.Danmaku.Live)).IsEqualTo((null, null));
		}
	}

	[Test]
	public async Task RowToggleSavesOnlyThatSettingAsync(CancellationToken cancellationToken)
	{
		FakeRoomAppService rooms = new(InitialOrder);
		using RoomListViewModel viewModel = Create(rooms);
		RoomItemViewModel room = viewModel.Rooms.Single(static room => room.RoomId == 3);
		RoomSettingsDto before = room.Settings;

		room.IsDanmakuEnabled = !before.IsDanmakuEnabled;

		await TestWait.UntilAsync(() => !rooms.Updates.IsEmpty, "the room settings are saved", cancellationToken);

		await Assert.That(rooms.Updates).IsEquivalentTo([before with { IsDanmakuEnabled = !before.IsDanmakuEnabled }], EqualityComparer<RoomSettingsDto>.Default);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task FailedSaveRestoresTheToggleAsync(bool failsSynchronously, CancellationToken cancellationToken)
	{
		// 不是本次取消引起的取消也算失败，例如请求超时
		TaskCanceledException failure = new(null, new TimeoutException());
		FakeRoomAppService rooms = new(InitialOrder) { UpdateFailure = failure, FailsSynchronously = failsSynchronously };
		ConcurrentQueue<Exception> reported = new();
		using RoomListViewModel viewModel = Create(rooms, reportSaveFailure: reported.Enqueue);
		RoomItemViewModel room = viewModel.Rooms.Single(static room => room.RoomId == 3);
		bool original = room.IsDanmakuEnabled;

		room.IsDanmakuEnabled = !original;

		// 先回滚再报告，报告到了时回滚已经完成
		await TestWait.UntilAsync(() => !reported.IsEmpty, "the failed save is reported", cancellationToken);
		Exception exception = await Assert.That(reported).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(exception).IsSameReferenceAs(failure);
			await Assert.That(rooms.Updates).Count().IsEqualTo(1).Because("回滚不应把旧值再保存一次");
			await Assert.That((room.IsDanmakuEnabled, room.Settings.IsDanmakuEnabled)).IsEqualTo((original, original));
		}
	}

	[Test]
	public async Task ShowingDanmakuSwitchesFromTheOpenFileToTheSelectedRoomAsync(CancellationToken cancellationToken)
	{
		FakeDanmakuAppService danmaku = new() { OpenFile = static (_, _) => Task.FromResult(DanmakuFileViewModelTest.CreateFile(seconds: [1])) };
		using RoomListViewModel viewModel = Create(new FakeRoomAppService(InitialOrder), danmaku);

		Select(viewModel, [1]);
		await viewModel.Danmaku.LoadFileAsync("a.xml", cancellationToken);
		viewModel.SelectedRooms.Clear();
		Select(viewModel, [4]);
		((ICommand)viewModel.ShowDanmakuCommand).Execute(null);

		await Assert.That((viewModel.Danmaku.IsOpen, viewModel.Danmaku.File, viewModel.Danmaku.Live?.RoomId)).IsEqualTo((true, null, 4));
	}

	// 即使面板正开着别的文件
	[Test]
	public async Task OpeningADanmakuFileFromTheRoomMenuStartsInTheRoomDirectoryAsync(CancellationToken cancellationToken)
	{
		FakeConfigManager configManager = new();
		string recordDirectory = (await configManager.GetAsync(cancellationToken)).RecordDirectory;
		FakeDanmakuAppService danmaku = new() { OpenFile = static (_, _) => Task.FromResult(DanmakuFileViewModelTest.CreateFile(seconds: [1])) };
		using RoomListViewModel viewModel = Create(new FakeRoomAppService(InitialOrder), danmaku, configManager);
		TaskCompletionSource<string> picked = new(TaskCreationOptions.RunContinuationsAsynchronously);
		viewModel.Danmaku.PickDanmakuFile = directory =>
		{
			picked.TrySetResult(directory);
			return Task.FromResult<string?>(null);
		};

		Select(viewModel, [1]);
		await viewModel.Danmaku.LoadFileAsync("a.xml", cancellationToken);
		viewModel.SelectedRooms.Clear();
		Select(viewModel, [5]);
		((ICommand)viewModel.OpenDanmakuFileCommand).Execute(null);

		await Assert.That(await picked.Task.WaitAsync(cancellationToken)).IsEqualTo(RecordingFileLayout.GetRoomDirectory(recordDirectory, 5));
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task LoggingInDismissesTheBannerAndMarksTheDanmakuConnectionAsPendingAsync(bool fromPanel, CancellationToken cancellationToken)
	{
		using BehaviorSignal<string?> cookie = new(null);
		using RoomListViewModel viewModel = CreateAnonymous(cookie, _ =>
		{
			cookie.OnNext("a=b");
			return Task.FromResult(true);
		});
		(string? banner, bool couldLogin) = (viewModel.LoginBannerText, viewModel.Danmaku.Live?.CanLogin is true);

		((ICommand)(fromPanel ? viewModel.Danmaku.LoginCommand : viewModel.LoginCommand)).Execute(null);

		// 面板的定时刷新在别的线程上也会写这两项，等两项都到位，不在中途断言
		await TestWait.UntilAsync(() => viewModel.LoginBannerText is null && IsWaitingForReconnection(viewModel), "the banner is dismissed and the panel waits for the reconnection", cancellationToken);

		await Assert.That((banner, couldLogin)).IsEqualTo((AppLanguage.Current.DanmakuMaskedBanner, true));
	}

	[Test]
	public async Task FailedLoginKeepsTheBannerAndTheLoginButtonAsync(CancellationToken cancellationToken)
	{
		using RoomListViewModel viewModel = CreateAnonymous(Signal.Return<string?>(null), static _ => Task.FromResult(false));

		bool loggedIn = await viewModel.LoginCommand.Execute().ToTask(cancellationToken);

		await Assert.That((loggedIn, viewModel.LoginBannerText, viewModel.Danmaku.Live?.CanLogin)).IsEqualTo((false, AppLanguage.Current.DanmakuMaskedBanner, true));
	}

	[Test]
	public async Task CookieChangedOnTheAccountPageUpdatesTheBannerAndTheDanmakuPanelAsync(CancellationToken cancellationToken)
	{
		using BehaviorSignal<string?> cookie = new(null);
		using RoomListViewModel viewModel = CreateAnonymous(cookie, static _ => Task.FromResult(false));
		(string? banner, bool couldLogin) = (viewModel.LoginBannerText, viewModel.Danmaku.Live?.CanLogin is true);

		cookie.OnNext(" a=b ");
		await TestWait.UntilAsync(() => viewModel.LoginBannerText is null && IsWaitingForReconnection(viewModel), "the login is picked up without a room change", cancellationToken);

		cookie.OnNext("  ");
		await TestWait.UntilAsync(() => viewModel.LoginBannerText == AppLanguage.Current.DanmakuMaskedBanner, "the logout is picked up without a room change", cancellationToken);

		await Assert.That((banner, couldLogin)).IsEqualTo((AppLanguage.Current.DanmakuMaskedBanner, true));
	}

	private static RoomListViewModel Create(FakeRoomAppService rooms, FakeDanmakuAppService? danmaku = null, FakeConfigManager? configManager = null, Action<Exception>? reportSaveFailure = null)
	{
		RoomListViewModel viewModel = new(rooms, configManager ?? new FakeConfigManager(), danmaku ?? new FakeDanmakuAppService()) { ReportSaveFailure = reportSaveFailure };
		((ICommand)viewModel.SortByCommand).Execute(RoomSortOrder.Custom);
		viewModel.Sync();
		return viewModel;
	}

	// cookie 一开始为 null，1 号直播间在录弹幕，横幅提示弹幕昵称被隐去；面板开着并跟随 1 号直播间
	private static RoomListViewModel CreateAnonymous(IObservable<string?> cookie, Func<CancellationToken, Task<bool>> showLogin)
	{
		FakeRoomAppService rooms = new(InitialOrder);
		rooms.Change(1, static snapshot => snapshot with { Danmaku = new RoomDanmakuSnapshot(true) });
		RoomListViewModel viewModel = new(rooms, new FakeConfigManager(), new FakeDanmakuAppService()) { ShowLogin = showLogin };
		viewModel.ApplyConfig(AppConfig.CreateDefault(), cookie);
		viewModel.Sync();
		viewModel.Danmaku.IsOpen = true;
		Select(viewModel, [1]);
		return viewModel;
	}

	private static bool IsWaitingForReconnection(RoomListViewModel viewModel)
	{
		return viewModel.Danmaku.Live is { CanLogin: false } live && live.IdentityText == AppLanguage.Current.DanmakuLoginPending;
	}

	private static void Select(RoomListViewModel viewModel, long[] roomIds)
	{
		foreach (long roomId in roomIds)
		{
			viewModel.SelectedRooms.Add(viewModel.Rooms.Single(room => room.RoomId == roomId));
		}
	}

	private static bool[] CanExecuteMoves(RoomListViewModel viewModel)
	{
		ICommand[] commands = [viewModel.MoveToTopCommand, viewModel.MoveToBottomCommand, viewModel.MoveUpCommand, viewModel.MoveDownCommand, viewModel.MoveSelectionToCommand];
		return [.. commands.Select(static command => command.CanExecute(0))];
	}

	private static async Task<IReadOnlyList<RoomItemViewModel>> ExecuteAsync(RoomListViewModel viewModel, Action execute, CancellationToken cancellationToken)
	{
		TaskCompletionSource<IReadOnlyList<RoomItemViewModel>> moved = new(TaskCreationOptions.RunContinuationsAsynchronously);
		using IDisposable subscription = viewModel.RoomsMoved.Subscribe(rooms => moved.TrySetResult(rooms));

		execute();
		return await moved.Task.WaitAsync(cancellationToken);
	}

	private static async Task AssertMovedAsync(RoomListViewModel viewModel, FakeRoomAppService rooms, IReadOnlyList<RoomItemViewModel> moved, long[] selected, long? before, long[] expected)
	{
		(long[] RoomIds, long? Before) call = await Assert.That(rooms.Moves).HasSingleItem();
		long[] selectedInOrder = [.. InitialOrder.Where(selected.Contains)];

		using (Assert.Multiple())
		{
			await Assert.That(call.RoomIds).IsEquivalentTo(selected);
			await Assert.That(call.Before).IsEqualTo(before);
			await Assert.That(viewModel.Rooms.Select(static room => room.RoomId)).IsEquivalentTo(expected, CollectionOrdering.Matching);
			await Assert.That(moved.Select(static room => room.RoomId)).IsEquivalentTo(selectedInOrder, CollectionOrdering.Matching);
			await Assert.That(viewModel.SelectedRooms.Select(static room => room.RoomId)).IsEquivalentTo(selected);
		}
	}

	private sealed class FakeRoomAppService(IEnumerable<long> roomIds) : RoomAppServiceStub
	{
		private readonly List<RoomSnapshot> _rooms = [.. roomIds.Select(static roomId => new RoomSnapshot(new RoomSettingsDto { RoomId = roomId }, 0, $"主播{roomId}", null, LiveStatus.Idle, RecordStatus.Idle, 0, null, null))];

		public ConcurrentQueue<(long[] RoomIds, long? Before)> Moves { get; } = new();

		public ConcurrentQueue<RoomSettingsDto> Updates { get; } = new();

		public Exception? UpdateFailure { get; init; }

		// 保存失败时不等任何异步操作就返回失败的任务，像真实的保存在拿锁之前就出错那样
		public bool FailsSynchronously { get; init; }

		public void Change(long roomId, Func<RoomSnapshot, RoomSnapshot> change)
		{
			int index = _rooms.FindIndex(room => room.RoomId == roomId);
			_rooms[index] = change(_rooms[index]);
		}

		public void Remove(long roomId)
		{
			_rooms.RemoveAll(room => room.RoomId == roomId);
		}

		public override IReadOnlyList<RoomSnapshot> GetRooms()
		{
			return [.. _rooms];
		}

		public override Task MoveAsync(IReadOnlyCollection<long> roomIds, long? before, CancellationToken cancellationToken = default)
		{
			Moves.Enqueue(([.. roomIds], before));
			RoomOrder.MoveBefore(_rooms, static room => room.RoomId, roomIds.ToHashSet(), before);
			return Task.CompletedTask;
		}

		public override Task UpdateAsync(RoomSettingsDto settings, CancellationToken cancellationToken = default)
		{
			Updates.Enqueue(settings);
			return UpdateFailure is { } failure && FailsSynchronously ? Task.FromException(failure) : SaveAsync(settings);
		}

		private async Task SaveAsync(RoomSettingsDto settings)
		{
			await Task.Yield();

			if (UpdateFailure is { } failure)
			{
				throw failure;
			}

			int index = _rooms.FindIndex(room => room.RoomId == settings.RoomId);
			_rooms[index] = _rooms[index] with { Settings = settings };
		}
	}
}
