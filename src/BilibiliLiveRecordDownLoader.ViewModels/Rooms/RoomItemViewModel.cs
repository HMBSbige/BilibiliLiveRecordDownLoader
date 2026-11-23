namespace BilibiliLiveRecordDownLoader.ViewModels.Rooms;

public sealed partial class RoomItemViewModel : ViewModelBase
{
	private readonly IRoomAppService _roomAppService;
	private readonly TimeProvider _timeProvider;
	private bool _isApplying;
	private bool _hasCookie;

	public RoomItemViewModel(RoomSnapshot snapshot, bool hasCookie, IRoomAppService roomAppService, TimeProvider timeProvider)
	{
		_roomAppService = roomAppService;
		_timeProvider = timeProvider;
		RoomId = snapshot.RoomId;
		Settings = snapshot.Settings;
		Apply(snapshot, hasCookie);

		this.WhenChanged(static viewModel => viewModel.IsRecordingEnabled, static viewModel => viewModel.IsNotificationEnabled, static viewModel => viewModel.IsDanmakuEnabled, static (recording, notification, danmaku) => (recording, notification, danmaku))
			.Skip(1)
			.Subscribe(OnTogglesChanged)
			.DisposeWith(Disposables);
	}

	public long RoomId { get; }

	// 开关保存失败并回滚后调用；为 null 时交给全局处理
	internal Action<Exception>? ReportSaveFailure { get; init; }

	public RoomSettingsDto Settings { get; private set; }

	// 在自定义顺序中的位置
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

	[Reactive]
	public partial Enum Status { get; private set; } = LiveStatus.Unknown;

	[Reactive]
	public partial string? StatusTip { get; private set; }

	[Reactive]
	public partial bool HasError { get; private set; }

	[Reactive]
	public partial bool IsRecording { get; private set; }

	// 筛选、汇总和排序只看视频
	[Reactive]
	public partial bool IsRecordingDanmakuOnly { get; private set; }

	[Reactive]
	public partial bool IsRecordingDanmaku { get; private set; }

	[Reactive]
	public partial string StreamInfo { get; private set; } = string.Empty;

	[Reactive]
	public partial string QualityText { get; private set; } = string.Empty;

	[Reactive]
	public partial bool IsQualityRestricted { get; private set; }

	[Reactive]
	public partial string? QualityRestrictionTip { get; private set; }

	// 读屏只读得到列表项上的说明；录不上的原因和画质受限不会同时出现
	[Reactive]
	public partial string? HelpText { get; private set; }

	[Reactive]
	public partial string RecordingInfo { get; private set; } = string.Empty;

	[Reactive]
	public partial bool IsRecordingEnabled { get; set; }

	[Reactive]
	public partial bool IsNotificationEnabled { get; set; }

	[Reactive]
	public partial bool IsDanmakuEnabled { get; set; }

	// hasCookie 是现在有没有配 Cookie，受限原因是按请求候选流时的身份判断的
	internal void Apply(RoomSnapshot snapshot, bool hasCookie)
	{
		_isApplying = true;
		_hasCookie = hasCookie;

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
			StatusTip = snapshot is { Recording: null, RecordStatus: RecordStatus.Connecting } ? snapshot.LastError : null;
			HasError = StatusTip is not null;
			IsRecording = snapshot.RecordStatus is RecordStatus.Recording;
			IsRecordingDanmaku = snapshot.Danmaku.IsRecording;
			IsRecordingDanmakuOnly = IsRecordingDanmaku && !IsRecording;
			(StreamInfo, QualityText, RecordingInfo) = snapshot.Recording is { } recording
				? ($"{StreamLabels.Of(recording.Format)} · {StreamLabels.Of(recording.Codec)}", StreamLabels.OfQuality(recording.Quality), $"{ByteSize.Format(snapshot.BytesPerSecond)}/s · {FormatDuration(recording.StartedAt)}")
				: (string.Empty, string.Empty, string.Empty);
			QualityRestrictionTip = snapshot.Recording is { Restriction: { } restriction } restricted ? DescribeRestriction(restriction, restricted.Quality, hasCookie) : null;
			IsQualityRestricted = QualityRestrictionTip is not null;
			HelpText = StatusTip ?? QualityRestrictionTip;
			IsRecordingEnabled = snapshot.Settings.IsRecordingEnabled;
			IsNotificationEnabled = snapshot.Settings.IsNotificationEnabled;
			IsDanmakuEnabled = snapshot.Settings.IsDanmakuEnabled;
		}
		finally
		{
			_isApplying = false;
		}
	}

	internal static string DescribeRestriction(QualityRestriction restriction, long quality, bool hasCookie)
	{
		return restriction switch
		{
			QualityRestriction.OtherCodec other => AppLanguage.Current.QualityRestrictedByCodec.ToString(StreamLabels.Of(other.Codec), StreamLabels.OfQuality(other.Quality)),
			QualityRestriction.OtherCodecUnavailable other => AppLanguage.Current.QualityRestrictedByCodecUnavailable.ToString(StreamLabels.Of(other.Codec), StreamLabels.OfQuality(other.Quality)),
			// 录制没断就不会重新请求候选流，登录之后仍是登录前的画质
			QualityRestriction.NotLoggedIn notLoggedIn when hasCookie => AppLanguage.Current.QualityRestrictedLoggedIn.ToString(StreamLabels.OfQuality(notLoggedIn.Highest)),
			QualityRestriction.NotLoggedIn notLoggedIn => AppLanguage.Current.QualityRestrictedNotLoggedIn.ToString(StreamLabels.OfQuality(quality), StreamLabels.OfQuality(notLoggedIn.Highest)),
			QualityRestriction.LoginMayHaveExpired expired => AppLanguage.Current.QualityRestrictedLoginMayHaveExpired.ToString(StreamLabels.OfQuality(expired.Highest), StreamLabels.OfQuality(quality))
		};
	}

	private string FormatDuration(DateTimeOffset startedAt)
	{
		TimeSpan elapsed = _timeProvider.GetUtcNow() - startedAt;

		return elapsed > TimeSpan.Zero ? string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalHours}:{elapsed:mm\\:ss}") : "0:00:00";
	}

	private void OnTogglesChanged((bool IsRecordingEnabled, bool IsNotificationEnabled, bool IsDanmakuEnabled) toggles)
	{
		if (_isApplying)
		{
			return;
		}

		RoomSettingsDto settings = Settings with { IsRecordingEnabled = toggles.IsRecordingEnabled, IsNotificationEnabled = toggles.IsNotificationEnabled, IsDanmakuEnabled = toggles.IsDanmakuEnabled };
		Settings = settings;

		Action<Exception> reportFailure = ReportSaveFailure ?? RxState.DefaultExceptionHandler.OnNext;
		Forget(SaveAsync);

		async Task SaveAsync(CancellationToken cancellationToken)
		{
			// 保存同步失败时，回滚嵌在这次变更通知里，其通知在 _isApplying 复位后才送达，旧值会再被保存一次
			await Task.Yield();

			try
			{
				await _roomAppService.UpdateAsync(settings, cancellationToken);
			}
			catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
			{
				if (_roomAppService.GetRooms().FirstOrDefault(room => room.RoomId == RoomId) is { } snapshot)
				{
					Apply(snapshot, _hasCookie);
				}

				reportFailure(exception);
			}
		}
	}
}
