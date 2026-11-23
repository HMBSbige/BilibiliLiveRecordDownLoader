namespace BilibiliLiveRecordDownLoader.ViewModels.Dialogs;

public sealed partial class FollowedRoomItemViewModel : ReactiveObject
{
	private readonly Action _onSelectionChanged;
	private readonly string _userName;

	public FollowedRoomItemViewModel(FollowedRoomDto room, Action onSelectionChanged)
	{
		_onSelectionChanged = onSelectionChanged;
		RoomId = room.RoomId;
		_userName = room.UserName;
		LiveStatus = room.LiveStatus;
		IsAdded = room.IsAdded;

		string roomId = RoomId.ToString(CultureInfo.InvariantCulture);
		DisplayName = _userName.Length > 0 ? _userName : roomId;
		Subtitle = room.Title.Length > 0 ? $"{roomId} · {room.Title}" : roomId;
	}

	public long RoomId { get; }

	public LiveStatus LiveStatus { get; }

	public string DisplayName { get; }

	public string Subtitle { get; }

	[Reactive]
	public partial bool IsAdded { get; private set; }

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
		return _userName.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) || RoomId.ToString(CultureInfo.InvariantCulture).Contains(keyword, StringComparison.Ordinal);
	}
}
