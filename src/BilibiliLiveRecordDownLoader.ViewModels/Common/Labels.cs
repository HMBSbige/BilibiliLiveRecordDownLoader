using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace BilibiliLiveRecordDownLoader.ViewModels.Common;

// 下拉框收起时读屏念的是数据项的 ToString，所以选项类型的 ToString 也用这里的文字
public static class Labels
{
	public static string Of(object? value)
	{
		return value switch
		{
			null => AppLanguage.Current.Inherit,
			true => AppLanguage.Current.Enabled,
			false => AppLanguage.Current.Disabled,
			RecordingSplitMode mode => mode switch
			{
				RecordingSplitMode.Duration => AppLanguage.Current.SplitByDuration,
				RecordingSplitMode.Size => AppLanguage.Current.SplitBySize,
				RecordingSplitMode.None => AppLanguage.Current.SplitNone
			},
			DanmakuGiftMode mode => mode switch
			{
				DanmakuGiftMode.Paid => AppLanguage.Current.GiftModePaid,
				DanmakuGiftMode.All => AppLanguage.Current.GiftModeAll,
				DanmakuGiftMode.None => AppLanguage.Current.GiftModeNone
			},
			StreamHostSelection selection => selection switch
			{
				StreamHostSelection.FirstResponse => AppLanguage.Current.HostFirstResponse,
				StreamHostSelection.FastestResponse => AppLanguage.Current.HostFastestResponse,
				StreamHostSelection.Random => AppLanguage.Current.HostRandom
			},
			AppTheme theme => theme switch
			{
				AppTheme.Light => AppLanguage.Current.ThemeLight,
				AppTheme.Dark => AppLanguage.Current.ThemeDark,
				AppTheme.FollowSystem => AppLanguage.Current.FollowSystem
			},
			LiveStatus status => status switch
			{
				LiveStatus.Idle => AppLanguage.Current.LiveStatusIdle,
				LiveStatus.Live => AppLanguage.Current.LiveStatusLive,
				LiveStatus.Round => AppLanguage.Current.LiveStatusRound,
				LiveStatus.Unknown => AppLanguage.Current.LiveStatusUnknown
			},
			RecordStatus status => status switch
			{
				RecordStatus.Connecting => AppLanguage.Current.RecordStatusConnecting,
				RecordStatus.Recording => AppLanguage.Current.RecordStatusRecording,
				RecordStatus.Idle => AppLanguage.Current.RecordStatusIdle
			},
			BackgroundTaskStatus status => status switch
			{
				BackgroundTaskStatus.Queued => AppLanguage.Current.TaskQueued,
				BackgroundTaskStatus.Running => AppLanguage.Current.TaskRunning,
				BackgroundTaskStatus.Completed => AppLanguage.Current.TaskCompleted,
				BackgroundTaskStatus.Failed => AppLanguage.Current.TaskFailed,
				BackgroundTaskStatus.Canceled => AppLanguage.Current.TaskCanceled
			},
			LogLevel level => level switch
			{
				LogLevel.Trace => AppLanguage.Current.LogLevelTrace,
				LogLevel.Debug => AppLanguage.Current.LogLevelDebug,
				LogLevel.Information => AppLanguage.Current.LogLevelInformation,
				LogLevel.Warning => AppLanguage.Current.LogLevelWarning,
				LogLevel.Error => AppLanguage.Current.LogLevelError,
				LogLevel.Critical => AppLanguage.Current.LogLevelCritical,
				LogLevel.None => string.Empty
			},
			RoomSortOrder sortOrder => sortOrder switch
			{
				RoomSortOrder.Name => AppLanguage.Current.SortByName,
				RoomSortOrder.RoomId => AppLanguage.Current.SortByRoomId,
				RoomSortOrder.Custom => AppLanguage.Current.SortByCustom,
				RoomSortOrder.Status => AppLanguage.Current.SortByStatus
			},
			RoomFilter filter => filter switch
			{
				RoomFilter.Live => AppLanguage.Current.FilterLive,
				RoomFilter.Recording => AppLanguage.Current.FilterRecording,
				RoomFilter.All => AppLanguage.Current.FilterAll
			},
			QrCodeLoginState state => state switch
			{
				QrCodeLoginState.WaitingForConfirm => AppLanguage.Current.QrCodeScanned,
				QrCodeLoginState.Expired => AppLanguage.Current.QrCodeExpired,
				QrCodeLoginState.Succeeded => AppLanguage.Current.LoginSucceeded,
				QrCodeLoginState.WaitingForScan => AppLanguage.Current.QrCodeScanHint
			},
			_ => value.ToString() ?? string.Empty
		};
	}
}
