namespace BilibiliLiveRecordDownLoader.ViewModels.Dialogs;

public sealed partial class FollowedRoomItemViewModel : ReactiveObject
{
	private readonly Action _onSelectionChanged;

	public FollowedRoomItemViewModel(FollowedRoomDto room, Action onSelectionChanged)
	{
		_onSelectionChanged = onSelectionChanged;
		RoomId = room.RoomId;
		UserName = room.UserName;
		LiveStatus = room.LiveStatus;
		IsAdded = room.IsAdded;

		string roomId = RoomId.ToString(CultureInfo.InvariantCulture);
		DisplayName = UserName.Length > 0 ? UserName : roomId;
		Subtitle = room.Title.Length > 0 ? $"{roomId} · {room.Title}" : roomId;
	}

	public long RoomId { get; }

	public string UserName { get; }

	public LiveStatus LiveStatus { get; }

	public string DisplayName { get; }

	public string Subtitle { get; }

	[Reactive]
	public partial bool IsAdded { get; private set; }

	// 已添加的房间不能选中
	public bool IsSelected
	{
		get;
		set
		{
			value &= !IsAdded;

			if (field == value)
			{
				return;
			}

			this.RaiseAndSetIfChanged(ref field, value);
			_onSelectionChanged();
		}
	}

	internal void MarkAdded()
	{
		IsAdded = true;
		IsSelected = false;
	}

	internal bool Matches(string keyword)
	{
		return UserName.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) || RoomId.ToString(CultureInfo.InvariantCulture).Contains(keyword, StringComparison.Ordinal);
	}
}
