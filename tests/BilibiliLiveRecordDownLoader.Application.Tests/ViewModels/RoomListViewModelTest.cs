using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.ViewModels.Rooms;
using Splat;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TUnit.Assertions.Enums;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class RoomListViewModelTest
{
	private static readonly long[] InitialOrder = [1, 2, 3, 4, 5];

	// 列表项读取 TimeProvider 时走全局定位器
	[Before(Class)]
	public static void RegisterServices()
	{
		AppLocator.CurrentMutable.RegisterConstant(TimeProvider.System);
	}

	[Test]
	[Arguments(RoomSortOrder.Custom, RoomFilter.All, true)]
	[Arguments(RoomSortOrder.Custom, RoomFilter.Live, false)]
	[Arguments(RoomSortOrder.Custom, RoomFilter.Recording, false)]
	[Arguments(RoomSortOrder.Status, RoomFilter.All, false)]
	[Arguments(RoomSortOrder.Name, RoomFilter.All, false)]
	[Arguments(RoomSortOrder.RoomId, RoomFilter.All, false)]
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
			await Assert.That(withoutSelection.Any(static canExecute => canExecute)).IsFalse();
			await Assert.That(CanExecuteMoves(viewModel)).IsEquivalentTo(Enumerable.Repeat(expected, 5), EqualityComparer<bool>.Default);
		}
	}

	[Test]
	[Timeout(30_000)]
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
		yield return () => ("Up", [3], 2, [1, 3, 2, 4, 5]);
		// 不连续的选中项合并到第一个选中项的上一位
		yield return () => ("Up", [4, 2], 1, [2, 4, 1, 3, 5]);
		yield return () => ("Up", [1, 3], 2, [1, 3, 2, 4, 5]);
		yield return () => ("Down", [3], 5, [1, 2, 4, 3, 5]);
		yield return () => ("Down", [2, 4], null, [1, 3, 5, 2, 4]);
		yield return () => ("Down", [5], null, [1, 2, 3, 4, 5]);
		yield return () => ("Top", [5, 3], 1, [3, 5, 1, 2, 4]);
		yield return () => ("Top", [1, 2], 3, [1, 2, 3, 4, 5]);
		yield return () => ("Bottom", [1, 3], null, [2, 4, 5, 1, 3]);
	}

	[Test]
	[Timeout(30_000)]
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
		yield return () => ([1], 3, 4, [2, 3, 1, 4, 5]);
		yield return () => ([2, 3], 0, 1, [2, 3, 1, 4, 5]);
		yield return () => ([2, 3], 5, null, [1, 4, 5, 2, 3]);
		// 放在被拖动的房间之间时，以后面第一个不移动的房间为准
		yield return () => ([2, 3], 2, 4, [1, 2, 3, 4, 5]);
		yield return () => ([4, 1], 3, 5, [2, 3, 1, 4, 5]);
	}

	[Test]
	[Timeout(30_000)]
	public async Task SortOrderIsRestoredFromAndSavedToTheConfigAsync(CancellationToken cancellationToken)
	{
		AppConfig config = AppConfig.CreateDefault();
		config.RoomSortOrder = RoomSortOrder.Name;
		FakeConfigManager configManager = new(config);
		using RoomListViewModel viewModel = new(new FakeRoomAppService(InitialOrder), configManager);

		viewModel.ApplyConfig(config);
		(RoomSortOrder restored, bool canReorder, int updates) = (viewModel.SortOrder, viewModel.CanReorder, configManager.UpdateCount);
		((ICommand)viewModel.SortByCommand).Execute(RoomSortOrder.Custom);

		while (config.RoomSortOrder is not RoomSortOrder.Custom)
		{
			await Task.Delay(10, cancellationToken);
		}

		using (Assert.Multiple())
		{
			await Assert.That(restored).IsEqualTo(RoomSortOrder.Name);
			await Assert.That(canReorder).IsFalse();
			// 读取配置时不回写
			await Assert.That(updates).IsEqualTo(0);
			await Assert.That(configManager.UpdateCount).IsEqualTo(1);
			await Assert.That(viewModel.CanReorder).IsTrue();
		}
	}

	private static RoomListViewModel Create(FakeRoomAppService rooms)
	{
		RoomListViewModel viewModel = new(rooms, new FakeConfigManager());
		((ICommand)viewModel.SortByCommand).Execute(RoomSortOrder.Custom);
		viewModel.Sync();
		return viewModel;
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
		viewModel.RoomsMoved += (_, rooms) => moved.TrySetResult(rooms);

		execute();
		return await moved.Task.WaitAsync(cancellationToken);
	}

	private static async Task AssertMovedAsync(RoomListViewModel viewModel, FakeRoomAppService rooms, IReadOnlyList<RoomItemViewModel> moved, long[] selected, long? before, long[] expected)
	{
		(long[] RoomIds, long? Before) call = rooms.Moves.Single();
		long[] selectedInOrder = [.. InitialOrder.Where(selected.Contains)];

		using (Assert.Multiple())
		{
			await Assert.That(call.RoomIds).IsEquivalentTo(selected, EqualityComparer<long>.Default);
			await Assert.That(call.Before).IsEqualTo(before);
			await Assert.That(viewModel.Rooms.Select(static room => room.RoomId).ToArray()).IsEquivalentTo(expected, EqualityComparer<long>.Default, CollectionOrdering.Matching);
			await Assert.That(moved.Select(static room => room.RoomId).ToArray()).IsEquivalentTo(selectedInOrder, EqualityComparer<long>.Default, CollectionOrdering.Matching);
			await Assert.That(viewModel.SelectedRooms.Select(static room => room.RoomId).ToArray()).IsEquivalentTo(selected, EqualityComparer<long>.Default);
		}
	}

	// 按应用服务的规则调整顺序，列表刷新后能看到结果
	private sealed class FakeRoomAppService(IEnumerable<long> roomIds) : IRoomAppService
	{
		private readonly List<RoomSnapshot> _rooms = [.. roomIds.Select(static roomId => new RoomSnapshot(new RoomSettingsDto { RoomId = roomId }, 0, $"主播{roomId}", null, LiveStatus.Idle, RecordStatus.Idle, 0, null, null))];

		public ConcurrentQueue<(long[] RoomIds, long? Before)> Moves { get; } = new();

		public long Version => 0;

		public bool IsRecording => false;

		public IReadOnlyList<RoomSnapshot> GetRooms()
		{
			return [.. _rooms];
		}

		public Task MoveAsync(IReadOnlyCollection<long> roomIds, long? before, CancellationToken cancellationToken = default)
		{
			Moves.Enqueue(([.. roomIds], before));
			RoomOrder.MoveBefore(_rooms, static room => room.RoomId, roomIds.ToHashSet(), before);
			return Task.CompletedTask;
		}

		public Task<RoomSnapshot> AddAsync(RoomSettingsDto settings, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task<IReadOnlyList<RoomAddResult>> AddRangeAsync(IReadOnlyList<RoomSettingsDto> settings, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public async IAsyncEnumerable<FollowedRoomDto> GetFollowedRoomsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			await Task.CompletedTask;
			yield break;
		}

		public Task UpdateAsync(RoomSettingsDto settings, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task RemoveAsync(IReadOnlyCollection<long> roomIds, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task RefreshAsync(IReadOnlyCollection<long> roomIds, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}
	}
}
