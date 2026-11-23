namespace BilibiliLiveRecordDownLoader.Views.Converters;

// 只用第一个值；其余的绑定只用于在切换语言时触发刷新
public sealed class LocalizedLabelConverter : IMultiValueConverter
{
	public static LocalizedLabelConverter Instance { get; } = new();

	public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
	{
		return GetLabel(values[0] is ChoiceOption option ? option.Value : values[0]);
	}

	private static string GetLabel(object? value)
	{
		return value switch
		{
			null => AppLanguage.Current.Inherit,
			true => AppLanguage.Current.Enabled,
			false => AppLanguage.Current.Disabled,
			StreamQuality quality => StreamLabels.Of(quality),
			RecorderType recorderType => recorderType switch
			{
				RecorderType.BuiltIn => AppLanguage.Current.RecorderBuiltIn,
				RecorderType.FFmpeg => AppLanguage.Current.RecorderFFmpeg
			},
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
