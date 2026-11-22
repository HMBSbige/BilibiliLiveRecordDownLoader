namespace BilibiliLiveRecordDownLoader.ViewModels.Rooms;

public sealed partial class RoomItemViewModel : ViewModelBase
{
	private readonly TimeProvider _timeProvider = AppLocator.Current.GetRequiredService<TimeProvider>();
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

	// 在应用服务返回的列表中的位置，即自定义顺序
	public int Order { get; internal set; }

	[Reactive]
	public partial string DisplayName { get; private set; } = string.Empty;

	[Reactive]
	public partial string Subtitle { get; private set; } = string.Empty;

	[Reactive]
	public partial string? Title { get; private set; }

	[Reactive]
	public partial LiveStatus LiveStatus { get; private set; }

	[Reactive]
	public partial RecordStatus RecordStatus { get; private set; }

	// 录制状态优先，其次是直播状态
	[Reactive]
	public partial Enum Status { get; private set; } = LiveStatus.Unknown;

	[Reactive]
	public partial string? StatusTip { get; private set; }

	[Reactive]
	public partial bool IsRecording { get; private set; }

	[Reactive]
	public partial string StreamInfo { get; private set; } = string.Empty;

	[Reactive]
	public partial string RecordingInfo { get; private set; } = string.Empty;

	[Reactive]
	public partial bool IsRecordingEnabled { get; set; }

	[Reactive]
	public partial bool IsNotificationEnabled { get; set; }

	internal void Apply(RoomSnapshot snapshot)
	{
		_isApplying = true;

		try
		{
			string roomId = RoomId.ToString(CultureInfo.InvariantCulture);
			string id = snapshot.ShortId > 0 ? string.Create(CultureInfo.InvariantCulture, $"{roomId} ({snapshot.ShortId})") : roomId;

			Settings = snapshot.Settings;
			(DisplayName, Subtitle) = (snapshot.UserName, snapshot.Title) switch
			{
				({ Length: > 0 } userName, { Length: > 0 } title) => (userName, $"{id} · {title}"),
				({ Length: > 0 } userName, _) => (userName, id),
				(_, var title) => (id, title ?? string.Empty)
			};
			Title = snapshot.Title;
			LiveStatus = snapshot.LiveStatus;
			RecordStatus = snapshot.RecordStatus;
			Status = snapshot.RecordStatus is RecordStatus.Idle ? snapshot.LiveStatus : snapshot.RecordStatus;
			StatusTip = snapshot.Recording is null ? snapshot.LastError : null;
			IsRecording = snapshot.RecordStatus is RecordStatus.Recording;
			(StreamInfo, RecordingInfo) = snapshot.Recording is { } recording
				? ($"{StreamLabels.Of(recording.Format)} · {StreamLabels.Of(recording.Codec)} · {StreamLabels.Of((StreamQuality)recording.Quality)}", $"{ByteSize.Format(snapshot.BytesPerSecond)}/s · {FormatDuration(recording.StartedAt)}")
				: (string.Empty, string.Empty);
			IsRecordingEnabled = snapshot.Settings.IsRecordingEnabled;
			IsNotificationEnabled = snapshot.Settings.IsNotificationEnabled;
		}
		finally
		{
			_isApplying = false;
		}
	}

	private string FormatDuration(DateTimeOffset startedAt)
	{
		TimeSpan elapsed = _timeProvider.GetUtcNow() - startedAt;

		return elapsed > TimeSpan.Zero ? string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalHours}:{elapsed:mm\\:ss}") : "0:00:00";
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
		Forget(UpdateAsync);

		async Task UpdateAsync(CancellationToken cancellationToken)
		{
			try
			{
				await roomAppService.UpdateAsync(settings, cancellationToken);
			}
			catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
			{
				// 保存失败时恢复为应用服务中的实际设置
				if (roomAppService.GetRooms().FirstOrDefault(snapshot => snapshot.RoomId == RoomId) is { } snapshot)
				{
					Apply(snapshot);
				}

				throw;
			}
		}
	}
}
