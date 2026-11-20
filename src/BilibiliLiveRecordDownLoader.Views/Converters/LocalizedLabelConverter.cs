namespace BilibiliLiveRecordDownLoader.Views.Converters;

public sealed class LocalizedLabelConverter : IValueConverter, IMultiValueConverter
{
	public static LocalizedLabelConverter Instance { get; } = new();

	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return GetLabel(value is ChoiceOption option ? option.Value : value);
	}

	public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
	{
		return Convert(values.Count > 0 ? values[0] : null, targetType, parameter, culture);
	}

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		throw new NotSupportedException();
	}

	private static string GetLabel(object? value)
	{
		return value switch
		{
			null => AppLanguage.Current.Inherit,
			true => AppLanguage.Current.Enabled,
			false => AppLanguage.Current.Disabled,
			StreamQuality quality => quality switch
			{
				StreamQuality.Smooth => AppLanguage.Current.QualitySmooth,
				StreamQuality.High => AppLanguage.Current.QualityHigh,
				StreamQuality.SuperHigh => AppLanguage.Current.QualitySuperHigh,
				StreamQuality.BluRay => AppLanguage.Current.QualityBluRay,
				StreamQuality.Original => AppLanguage.Current.QualityOriginal,
				StreamQuality.UltraHd4K => AppLanguage.Current.QualityUltraHd4K,
				StreamQuality.Dolby => AppLanguage.Current.QualityDolby,
				_ => quality.ToString()
			},
			RecorderType recorderType => recorderType switch
			{
				RecorderType.BuiltIn => AppLanguage.Current.RecorderBuiltIn,
				RecorderType.FFmpeg => AppLanguage.Current.RecorderFFmpeg,
				_ => recorderType.ToString()
			},
			StreamHostSelection selection => selection switch
			{
				StreamHostSelection.FirstResponse => AppLanguage.Current.HostFirstResponse,
				StreamHostSelection.FastestResponse => AppLanguage.Current.HostFastestResponse,
				StreamHostSelection.Random => AppLanguage.Current.HostRandom,
				_ => selection.ToString()
			},
			AppTheme theme => theme switch
			{
				AppTheme.Light => AppLanguage.Current.ThemeLight,
				AppTheme.Dark => AppLanguage.Current.ThemeDark,
				_ => AppLanguage.Current.FollowSystem
			},
			LiveStatus status => status switch
			{
				LiveStatus.Idle => AppLanguage.Current.LiveStatusIdle,
				LiveStatus.Live => AppLanguage.Current.LiveStatusLive,
				LiveStatus.Round => AppLanguage.Current.LiveStatusRound,
				_ => AppLanguage.Current.LiveStatusUnknown
			},
			RecordStatus status => status switch
			{
				RecordStatus.Connecting => AppLanguage.Current.RecordStatusConnecting,
				RecordStatus.Recording => AppLanguage.Current.RecordStatusRecording,
				_ => AppLanguage.Current.RecordStatusIdle
			},
			BackgroundTaskStatus status => status switch
			{
				BackgroundTaskStatus.Queued => AppLanguage.Current.TaskQueued,
				BackgroundTaskStatus.Running => AppLanguage.Current.TaskRunning,
				BackgroundTaskStatus.Completed => AppLanguage.Current.TaskCompleted,
				BackgroundTaskStatus.Failed => AppLanguage.Current.TaskFailed,
				_ => AppLanguage.Current.TaskCanceled
			},
			QrCodeLoginState state => state switch
			{
				QrCodeLoginState.WaitingForConfirm => AppLanguage.Current.QrCodeScanned,
				QrCodeLoginState.Expired => AppLanguage.Current.QrCodeExpired,
				QrCodeLoginState.Succeeded => AppLanguage.Current.LoginSucceeded,
				_ => AppLanguage.Current.QrCodeScanHint
			},
			_ => value.ToString() ?? string.Empty
		};
	}
}
