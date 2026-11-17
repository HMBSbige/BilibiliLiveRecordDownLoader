namespace BilibiliLiveRecordDownLoader.ViewModels.Rooms;

public sealed partial class RoomItemViewModel : ViewModelBase
{
	private bool _isApplying;

	public RoomItemViewModel(RoomSnapshot snapshot)
	{
		RoomId = snapshot.RoomId;
		Settings = snapshot.Settings;
		Apply(snapshot);

		this.WhenChanged(static viewModel => viewModel.IsRecordingEnabled, static viewModel => viewModel.IsNotificationEnabled, static (recording, notification) => (recording, notification))
			.Skip(1)
			.Subscribe(OnTogglesChanged)
			.DisposeWith(Disposables);
	}

	public long RoomId { get; }

	public RoomSettingsDto Settings { get; private set; }

	[Reactive]
	public partial long? ShortId { get; private set; }

	[Reactive]
	public partial string? UserName { get; private set; }

	[Reactive]
	public partial string? Title { get; private set; }

	[Reactive]
	public partial LiveStatus LiveStatus { get; private set; }

	[Reactive]
	public partial RecordStatus RecordStatus { get; private set; }

	[Reactive]
	public partial string Speed { get; private set; } = string.Empty;

	[Reactive]
	public partial bool IsRecordingEnabled { get; set; }

	[Reactive]
	public partial bool IsNotificationEnabled { get; set; }

	public string DisplayName => string.IsNullOrEmpty(UserName) ? RoomId.ToString(CultureInfo.InvariantCulture) : UserName;

	internal void Apply(RoomSnapshot snapshot)
	{
		_isApplying = true;

		try
		{
			Settings = snapshot.Settings;
			ShortId = snapshot.ShortId > 0 ? snapshot.ShortId : null;
			UserName = snapshot.UserName;
			Title = snapshot.Title;
			LiveStatus = snapshot.LiveStatus;
			RecordStatus = snapshot.RecordStatus;
			Speed = snapshot.RecordStatus is RecordStatus.Recording ? ByteSize.Format(snapshot.BytesPerSecond) + "/s" : string.Empty;
			IsRecordingEnabled = snapshot.Settings.IsRecordingEnabled;
			IsNotificationEnabled = snapshot.Settings.IsNotificationEnabled;
		}
		finally
		{
			_isApplying = false;
		}
	}

	private void OnTogglesChanged((bool IsRecordingEnabled, bool IsNotificationEnabled) toggles)
	{
		if (_isApplying)
		{
			return;
		}

		RoomSettingsDto settings = Settings with { IsRecordingEnabled = toggles.IsRecordingEnabled, IsNotificationEnabled = toggles.IsNotificationEnabled };
		Settings = settings;

		IRoomAppService roomAppService = AppLocator.Current.GetRequiredService<IRoomAppService>();
		Forget(cancellationToken => roomAppService.UpdateAsync(settings, cancellationToken));
	}
}
