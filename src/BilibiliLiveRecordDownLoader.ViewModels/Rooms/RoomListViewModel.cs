namespace BilibiliLiveRecordDownLoader.ViewModels.Rooms;

public sealed partial class RoomListViewModel : ViewModelBase
{
	private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(500);

	public RoomListViewModel()
	{
		AddRoomCommand.DisposeWith(Disposables);
		EditRoomCommand.DisposeWith(Disposables);
		RemoveRoomsCommand.DisposeWith(Disposables);
		RefreshRoomsCommand.DisposeWith(Disposables);
		OpenRoomFolderCommand.DisposeWith(Disposables);
		OpenLiveRoomCommand.DisposeWith(Disposables);
	}

	public ObservableCollection<RoomItemViewModel> Rooms { get; } = [];

	[Reactive]
	public partial bool HasRooms { get; private set; }

	internal void StartMonitoring()
	{
		IRoomAppService roomAppService = AppLocator.Current.GetRequiredService<IRoomAppService>();
		PollChanges(() => roomAppService.Version, () => Sync(roomAppService.GetRooms()), RefreshInterval);
	}

	[ReactiveCommand]
	private async Task AddRoomAsync(CancellationToken cancellationToken)
	{
		AppConfig config = await AppLocator.Current.GetRequiredService<IAppConfigManager>().GetAsync(cancellationToken);
		RoomSettingsDto? settings = await AppLocator.Current.GetRequiredService<DialogHostViewModel>().ShowAsync(new RoomEditorViewModel(null, config));

		if (settings is null)
		{
			return;
		}

		IRoomAppService roomAppService = AppLocator.Current.GetRequiredService<IRoomAppService>();
		await roomAppService.AddAsync(settings, cancellationToken);
		Sync(roomAppService.GetRooms());
	}

	[ReactiveCommand]
	private async Task EditRoomAsync(RoomItemViewModel? room, CancellationToken cancellationToken)
	{
		if (room is null)
		{
			return;
		}

		AppConfig config = await AppLocator.Current.GetRequiredService<IAppConfigManager>().GetAsync(cancellationToken);
		RoomSettingsDto? settings = await AppLocator.Current.GetRequiredService<DialogHostViewModel>().ShowAsync(new RoomEditorViewModel(room.Settings, config));

		if (settings is not null)
		{
			await AppLocator.Current.GetRequiredService<IRoomAppService>().UpdateAsync(settings, cancellationToken);
		}
	}

	[ReactiveCommand]
	private async Task RemoveRoomsAsync(IList? selection, CancellationToken cancellationToken)
	{
		RoomItemViewModel[] rooms = Select(selection);

		if (rooms.Length is 0)
		{
			return;
		}

		string names = string.Join(Environment.NewLine, rooms.Select(static room => room.DisplayName));

		if (await AppLocator.Current.GetRequiredService<DialogHostViewModel>().ConfirmAsync(AppLanguage.Current.RemoveRoomsConfirm, names))
		{
			await AppLocator.Current.GetRequiredService<IRoomAppService>().RemoveAsync([.. rooms.Select(static room => room.RoomId)], cancellationToken);
		}
	}

	[ReactiveCommand]
	private async Task RefreshRoomsAsync(IList? selection, CancellationToken cancellationToken)
	{
		RoomItemViewModel[] rooms = Select(selection);
		IRoomAppService roomAppService = AppLocator.Current.GetRequiredService<IRoomAppService>();

		IEnumerable<RoomItemViewModel> targets = rooms.Length is 0 ? Rooms : rooms;

		await roomAppService.RefreshAsync([.. targets.Select(static room => room.RoomId)], cancellationToken);
	}

	[ReactiveCommand]
	private async Task OpenRoomFolderAsync(RoomItemViewModel? room, CancellationToken cancellationToken)
	{
		if (room is null)
		{
			return;
		}

		AppConfig config = await AppLocator.Current.GetRequiredService<IAppConfigManager>().GetAsync(cancellationToken);
		string directory = RecordingFileLayout.GetRoomDirectory(config.RecordDirectory, room.RoomId);

		await AppLocator.Current.GetRequiredService<ILauncherService>().OpenFolderAsync(directory);
	}

	[ReactiveCommand]
	private async Task OpenLiveRoomAsync(RoomItemViewModel? room)
	{
		if (room is null)
		{
			return;
		}

		await AppLocator.Current.GetRequiredService<ILauncherService>().LaunchUriAsync(new Uri(AppConsts.LiveRoomUrlPrefix + room.RoomId.ToString(CultureInfo.InvariantCulture)));
	}

	private void Sync(IReadOnlyList<RoomSnapshot> snapshots)
	{
		Dictionary<long, RoomSnapshot> latest = [];

		foreach (RoomSnapshot snapshot in snapshots)
		{
			latest[snapshot.RoomId] = snapshot;
		}

		for (int index = Rooms.Count - 1; index >= 0; --index)
		{
			if (!latest.ContainsKey(Rooms[index].RoomId))
			{
				Rooms[index].Dispose();
				Rooms.RemoveAt(index);
			}
		}

		foreach (RoomSnapshot snapshot in latest.Values)
		{
			if (Rooms.FirstOrDefault(room => room.RoomId == snapshot.RoomId) is { } room)
			{
				room.Apply(snapshot);
			}
			else
			{
				Rooms.Add(new RoomItemViewModel(snapshot));
			}
		}

		HasRooms = Rooms.Count > 0;
	}

	private static RoomItemViewModel[] Select(IList? selection)
	{
		return selection is null ? [] : [.. selection.OfType<RoomItemViewModel>()];
	}
}
