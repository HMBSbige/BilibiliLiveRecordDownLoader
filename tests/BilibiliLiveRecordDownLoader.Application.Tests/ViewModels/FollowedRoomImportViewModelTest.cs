using BilibiliLiveRecordDownLoader.Application.Contracts.Accounts;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using BilibiliLiveRecordDownLoader.ViewModels.Dialogs;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class FollowedRoomImportViewModelTest
{
	private static readonly FollowedRoomDto[] Followed =
	[
		new(6154037, "Asaki大人", "GTA6", LiveStatus.Live, false),
		new(5440, "官方", string.Empty, LiveStatus.Idle, true),
		new(22907643, "Asahi", "chat", LiveStatus.Idle, false),
		new(7777, "其他", string.Empty, LiveStatus.Round, false)
	];

	[Test]
	public async Task SearchMatchesStreamerNameOrRoomIdAsync()
	{
		using FollowedRoomImportViewModel dialog = Open(new FakeRoomAppService(Followed));

		dialog.SearchText = " asa ";
		long[] byName = VisibleRoomIds(dialog);
		dialog.SearchText = "544";
		long[] byRoomId = VisibleRoomIds(dialog);
		dialog.SearchText = null;
		long[] all = VisibleRoomIds(dialog);

		using (Assert.Multiple())
		{
			await Assert.That(dialog.State).IsEqualTo(FollowedRoomLoadState.Loaded);
			await Assert.That(byName).IsEquivalentTo([6154037L, 22907643]);
			await Assert.That(byRoomId).IsEquivalentTo([5440L]);
			await Assert.That(all).Count().IsEqualTo(Followed.Length);
		}
	}

	[Test]
	public async Task SelectAllOnlySelectsVisibleRoomsThatAreNotAddedAsync()
	{
		using FollowedRoomImportViewModel dialog = Open(new FakeRoomAppService(Followed));

		dialog.SearchText = "a";
		dialog.AreAllVisibleSelected = true;
		bool? afterSelectAll = dialog.AreAllVisibleSelected;
		long[] selectedAfterSelectAll = [.. dialog.VisibleRooms.Where(static room => room.IsSelected).Select(static room => room.RoomId)];

		dialog.SearchText = "7777";
		bool? afterSearch = dialog.AreAllVisibleSelected;
		dialog.SearchText = null;
		bool? withAll = dialog.AreAllVisibleSelected;

		using (Assert.Multiple())
		{
			await Assert.That(selectedAfterSelectAll).IsEquivalentTo([6154037L, 22907643]);
			await Assert.That(afterSelectAll).IsTrue();
			await Assert.That(afterSearch).IsFalse();
			await Assert.That(withAll).IsNull();
			await Assert.That(dialog.SelectedCount).IsEqualTo(2);
			await Assert.That(dialog.ImportText).IsEqualTo(AppLanguage.Current.ImportCount.ToString("2"));
		}
	}

	[Test]
	public async Task AddedRoomsCannotBeSelectedAsync()
	{
		using FollowedRoomImportViewModel dialog = Open(new FakeRoomAppService(Followed));
		FollowedRoomItemViewModel added = dialog.VisibleRooms.Single(static room => room.IsAdded);

		added.IsSelected = true;
		dialog.SearchText = "5440";
		dialog.AreAllVisibleSelected = true;

		using (Assert.Multiple())
		{
			await Assert.That(added.IsSelected).IsFalse();
			await Assert.That(dialog.SelectedCount).IsEqualTo(0);
			await Assert.That(dialog.HasSelectableVisibleRooms).IsFalse();
			await Assert.That(dialog.AreAllVisibleSelected).IsFalse();
			await Assert.That(((ICommand)dialog.ImportCommand).CanExecute(null)).IsFalse();
		}
	}

	[Test]
	[Arguments(true, true, false)]
	[Arguments(false, false, true)]
	public async Task ImportAppliesTheTogglesToEveryRoomAndClosesAsync(bool isRecordingEnabled, bool isDanmakuEnabled, bool isNotificationEnabled, CancellationToken cancellationToken)
	{
		FakeRoomAppService rooms = new(Followed);
		RecordingNotifications notifications = new();
		using FollowedRoomImportViewModel dialog = Open(rooms, notifications);
		(bool Recording, bool Danmaku, bool Notification) defaults = (dialog.IsRecordingEnabled, dialog.IsDanmakuEnabled, dialog.IsNotificationEnabled);

		Select(dialog, 6154037, 22907643);
		dialog.IsRecordingEnabled = isRecordingEnabled;
		dialog.IsDanmakuEnabled = isDanmakuEnabled;
		dialog.IsNotificationEnabled = isNotificationEnabled;
		((ICommand)dialog.ImportCommand).Execute(null);
		bool result = await dialog.Result.WaitAsync(cancellationToken);
		RoomSettingsDto[] added = await Assert.That(rooms.Added).HasSingleItem();
		ShownNotification notification = await Assert.That(notifications.Shown).HasSingleItem();

		RoomSettingsDto template = new() { RoomId = 0, IsRecordingEnabled = isRecordingEnabled, IsDanmakuEnabled = isDanmakuEnabled, IsNotificationEnabled = isNotificationEnabled };

		using (Assert.Multiple())
		{
			await Assert.That(defaults).IsEqualTo((RoomDefaults.IsRecordingEnabled, DanmakuDefaults.IsEnabled, false));
			await Assert.That(result).IsTrue();
			await Assert.That(added).IsEquivalentTo([template with { RoomId = 6154037 }, template with { RoomId = 22907643 }], EqualityComparer<RoomSettingsDto>.Default, CollectionOrdering.Matching);
			await Assert.That(notification.Type).IsEqualTo(AppNotificationType.Success);
		}
	}

	[Test]
	public async Task PartialFailureKeepsTheDialogOpenWithTheFailedRoomsSelectedAsync(CancellationToken cancellationToken)
	{
		FakeRoomAppService rooms = new(Followed)
		{
			AddRange = static settings => [.. settings.Select(static item => item.RoomId is 7777 ? new RoomAddResult.Failed(item.RoomId, "boom") : (RoomAddResult)new RoomAddResult.Skipped(item.RoomId))]
		};
		using FollowedRoomImportViewModel dialog = Open(rooms);

		Select(dialog, 6154037, 7777);
		((ICommand)dialog.ImportCommand).Execute(null);
		await TestWait.UntilAsync(() => dialog is { IsBusy: false, ErrorMessage: not null }, "the failed import shows its error", cancellationToken);

		FollowedRoomItemViewModel skipped = dialog.VisibleRooms.Single(static room => room.RoomId is 6154037);

		using (Assert.Multiple())
		{
			await Assert.That(dialog.Result.IsCompleted).IsFalse();
			await Assert.That(dialog.ErrorMessage).Contains(AppLanguage.Current.NamedReason.ToString("其他", "boom"));
			await Assert.That(skipped.IsAdded).IsTrue();
			await Assert.That(SelectedRoomIds(dialog)).IsEquivalentTo([7777L]);
		}
	}

	[Test]
	public async Task NotLoggedInAsksToLogInAsync()
	{
		int loginRequests = 0;
		using FollowedRoomImportViewModel dialog = Open(new FakeRoomAppService(Followed) { Failure = new LoginRequiredException() }, showLogin: () => ++loginRequests);

		FollowedRoomLoadState state = dialog.State;
		((ICommand)dialog.LoginCommand).Execute(null);

		using (Assert.Multiple())
		{
			await Assert.That(state).IsEqualTo(FollowedRoomLoadState.LoginRequired);
			await Assert.That(loginRequests).IsEqualTo(1);
			await Assert.That(dialog.Result.IsCompleted).IsTrue();
		}
	}

	[Test]
	public async Task FailedLoadingShowsTheErrorAndCanBeRetriedAsync()
	{
		FakeRoomAppService rooms = new(Followed) { Failure = new InvalidOperationException("加载失败") };
		using FollowedRoomImportViewModel dialog = Open(rooms);

		(FollowedRoomLoadState state, string? error) = (dialog.State, dialog.LoadError);
		rooms.Failure = null;
		((ICommand)dialog.ReloadCommand).Execute(null);

		using (Assert.Multiple())
		{
			await Assert.That(state).IsEqualTo(FollowedRoomLoadState.Failed);
			await Assert.That(error).IsEqualTo("加载失败");
			await Assert.That(dialog.State).IsEqualTo(FollowedRoomLoadState.Loaded);
			await Assert.That(dialog.VisibleRooms).Count().IsEqualTo(Followed.Length);
		}
	}

	[Test]
	public async Task AnEmptyFollowingListIsShownAsEmptyAsync()
	{
		using FollowedRoomImportViewModel dialog = Open(new FakeRoomAppService([]));

		await Assert.That((dialog.IsEmpty, dialog.HasRooms)).IsEqualTo((true, false));
	}

	// 假的应用服务同步返回关注列表，打开后即加载完成
	private static FollowedRoomImportViewModel Open(FakeRoomAppService rooms, RecordingNotifications? notifications = null, Action? showLogin = null)
	{
		FollowedRoomImportViewModel dialog = new(rooms, notifications ?? new RecordingNotifications(), showLogin ?? (static () => { }));
		dialog.OnOpened();
		return dialog;
	}

	private static void Select(FollowedRoomImportViewModel dialog, params long[] roomIds)
	{
		foreach (FollowedRoomItemViewModel room in dialog.VisibleRooms.Where(room => roomIds.Contains(room.RoomId)))
		{
			room.IsSelected = true;
		}
	}

	private static long[] VisibleRoomIds(FollowedRoomImportViewModel dialog)
	{
		return [.. dialog.VisibleRooms.Select(static room => room.RoomId)];
	}

	private static long[] SelectedRoomIds(FollowedRoomImportViewModel dialog)
	{
		dialog.SearchText = null;
		return [.. dialog.VisibleRooms.Where(static room => room.IsSelected).Select(static room => room.RoomId)];
	}

	private sealed class FakeRoomAppService(IReadOnlyList<FollowedRoomDto> followed) : RoomAppServiceStub
	{
		public Exception? Failure { get; set; }

		public Func<IReadOnlyList<RoomSettingsDto>, IReadOnlyList<RoomAddResult>> AddRange { get; init; } = static settings => [.. settings.Select(static item => new RoomAddResult.Added(item.RoomId))];

		public ConcurrentQueue<RoomSettingsDto[]> Added { get; } = new();

		public override Task<IReadOnlyList<RoomAddResult>> AddRangeAsync(IReadOnlyList<RoomSettingsDto> settings, CancellationToken cancellationToken = default)
		{
			Added.Enqueue([.. settings]);
			return Task.FromResult(AddRange(settings));
		}

		public override async IAsyncEnumerable<FollowedRoomDto> GetFollowedRoomsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			if (Failure is { } failure)
			{
				throw failure;
			}

			foreach (FollowedRoomDto room in followed)
			{
				yield return room;
			}

			await Task.CompletedTask;
		}
	}
}
